using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography.Certificates;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Web.Http.Headers;
using XFiles.FileSystem;

namespace XFiles.Services
{
    /// <summary>
    /// Client for the Xbox Developer Mode Device Portal REST API.
    /// Probe phase: layered connectivity diagnostic — internet reachability, raw TCP,
    /// plain-HTTP and HTTPS paths — to determine empirically how (or whether) the app
    /// running on the console can reach its own portal.
    /// Credentials come from DevPortalSecrets.g.cs (generated from .env at build time).
    /// </summary>
    internal static class DevicePortalService
    {
        private const int HttpsPort = 11443;
        private const int HttpPort = 80;
        private const string KnownFoldersPath = "/api/filesystem/apps/knownfolders";
        private const string InternetProbeHost = "www.msftconnecttest.com";
        private const string InternetProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";

        private static readonly object Sync = new object();
        private static bool _probeRan;
        private static string _baseUrl;
        private static string _probeStatus = "not run";

        public static event Action ProbeCompleted;

        public static bool HasCredentials =>
            !string.IsNullOrEmpty(DevPortalSecrets.User) && !string.IsNullOrEmpty(DevPortalSecrets.Password);

        public static string BaseUrl => _baseUrl;

        public static string ProbeStatus => _probeStatus;

        /// <summary>
        /// Fire-and-forget probe, safe to call repeatedly. Logs results to the
        /// central log viewer. Runs once per process unless force is set (About + Y).
        /// </summary>
        public static void ProbeAsync(bool force = false)
        {
            lock (Sync)
            {
                if (_probeRan && !force)
                {
                    Log.Dbg("DevicePortal.Probe: already ran, skipping");
                    return;
                }
                _probeRan = true;
            }

            _probeStatus = "probing…";
            _ = ProbeCoreAsync();
        }

        private static async Task ProbeCoreAsync()
        {
            if (!HasCredentials)
            {
                _probeStatus = "no credentials";
                Log.Warn("DevicePortal.Probe: no credentials — configure .env (DEV_PORTAL_USER/DEV_PORTAL_PASS)");
                ProbeCompleted?.Invoke();
                return;
            }

            var hosts = GetCandidateHosts();
            Log.Info("DevicePortal.Probe: candidates = {Hosts}", string.Join(", ", hosts));

            LogConnectionProfile();
            await TestInternetAsync();

            // Self-liberation test: NetworkIsolationSetAppContainerConfig (the API under
            // checknetisolation) can be called by AppContainers with network capabilities,
            // and dev mode bypasses the admin check. Add own SID to the loopback exemption
            // list. If it returns 0, the portal tests below should now pass without SSH.
            Log.Info("DevicePortal.Probe: self-exempt — NetworkIsolationSetAppContainerConfig");
            uint exemptHr = SelfExempt();
            Log.Info("DevicePortal.Probe: self-exempt HRESULT 0x{HR:X8} ({Label})",
                exemptHr, exemptHr == 0 ? "OK — exemption applied" : "FAILED — need SSH one-liner");

            // Decisive experiment: is the console's own SSH reachable from the app WITHOUT
            // loopback exemption? If TCP LAN_IP:22 succeeds, an in-app SSH client could
            // auto-apply the exemption at startup (self-liberation). If dropped, chicken-egg.
            var lanHost = hosts.FirstOrDefault();
            if (lanHost != null)
            {
                Log.Info("DevicePortal.Probe: SSH reachability test — {Host}:22", lanHost);
                await TestTcpAsync(lanHost, 22);
            }

            foreach (var host in hosts)
            {
                var tcpHttps = await TestTcpAsync(host, HttpsPort);
                var tcpHttp = await TestTcpAsync(host, HttpPort);

                if (tcpHttps)
                    await TestHttpGetAsync(host, HttpsPort, useTls: true);
                if (tcpHttp)
                    await TestHttpGetAsync(host, HttpPort, useTls: false);
            }

            if (_baseUrl != null)
            {
                _probeStatus = "OK " + _baseUrl;
                Log.Info("DevicePortal.Probe: WORKING base URL: {BaseUrl}", _baseUrl);
                await DeepProbeAsync(_baseUrl);
            }
            else
            {
                _probeStatus = "portal unreachable";
                Log.Warn("DevicePortal.Probe: no path works — see per-test results above");
                Log.Warn("DevicePortal.Probe: if on Xbox dev mode, re-apply loopback exemption via SSH: " +
                    "checknetisolation loopbackexempt -a -n=XFiles.Xbox_jgz7qwhvc5jpc, then press Y in About");
            }

            await RunFileSystemProbeAsync();
            ProbeCompleted?.Invoke();
        }

        /// <summary>
        /// Filesystem diagnostic: map how far the sandbox can reach on the Xbox drives.
        /// Mirrors the portal's known-folders layout (Q:\Users\...\AppData\Local\Packages)
        /// using the same *FromApp P/Invoke the browser uses, logging Win32 errors per step.
        /// </summary>
        private static async Task RunFileSystemProbeAsync()
        {
            Log.Info("DevicePortal.Probe: FS probe — enumerate drives");

            List<FileEntry> root;
            try
            {
                root = await DirectoryScanner.ScanAsync(null);
            }
            catch (Exception ex)
            {
                Log.Warn("DevicePortal.Probe: FS root scan exception: {Message}", ex.Message);
                return;
            }

            var drives = root.Where(e => e.IsDrive).Select(e => e.FullPath).ToList();
            Log.Info("DevicePortal.Probe: FS drives = {Drives}", string.Join(", ", drives));

            foreach (var drive in drives)
                ProbeDir(drive, "drive");

            // Portal-known locations regardless of drive enumeration.
            ProbeDir("Q:\\Users", "portal-known");
            ProbeDir("D:\\DevelopmentFiles", "portal-known");
        }

        private static void ProbeDir(string path, string label)
        {
            var names = DirectoryScanner.EnumerateDirectoryNames(path, out int err);
            if (err != 0)
            {
                Log.Warn("DevicePortal.Probe: FS {Label} '{Path}' ERROR {Err}: {Desc}",
                    label, path, err, DescribeWin32(err));
                return;
            }

            Log.Info("DevicePortal.Probe: FS {Label} '{Path}' OK — {Count} entries: {First}",
                label, path, names.Count, string.Join(", ", names.Take(25)));

            if (label != "drive") return;

            // Walk the portal's known-folders shape: X:\Users\<user>\AppData\Local\Packages
            if (names.Any(n => n.Equals("Users", StringComparison.OrdinalIgnoreCase)))
            {
                var users = DirectoryScanner.EnumerateDirectoryNames(path + "\\Users", out int uerr);
                if (uerr != 0)
                {
                    Log.Warn("DevicePortal.Probe: FS 'Users' ERROR {Err}: {Desc}", uerr, DescribeWin32(uerr));
                    return;
                }
                Log.Info("DevicePortal.Probe: FS 'Users' OK — {Count}: {First}",
                    users.Count, string.Join(", ", users.Take(10)));

                foreach (var user in users)
                {
                    var packages = path + "\\Users\\" + user + "\\AppData\\Local\\Packages";
                    var pkgs = DirectoryScanner.EnumerateDirectoryNames(packages, out int perr);
                    if (perr != 0)
                    {
                        Log.Warn("DevicePortal.Probe: FS Packages '{P}' ERROR {Err}: {Desc}",
                            packages, perr, DescribeWin32(perr));
                        continue;
                    }
                    Log.Info("DevicePortal.Probe: FS Packages '{P}' OK — {Count}: {First}",
                        packages, pkgs.Count, string.Join(", ", pkgs.Take(25)));

                    // Depth probe: LocalState of the first few packages + read test of one file.
                    foreach (var pkg in pkgs.Take(3))
                    {
                        var localState = packages + "\\" + pkg + "\\LocalState";
                        var lsNames = DirectoryScanner.EnumerateDirectoryNames(localState, out int lerr);
                        if (lerr != 0)
                        {
                            Log.Warn("DevicePortal.Probe: FS LocalState '{LS}' ERROR {Err}: {Desc}",
                                localState, lerr, DescribeWin32(lerr));
                            continue;
                        }
                        Log.Info("DevicePortal.Probe: FS LocalState '{LS}' OK — {Count}: {First}",
                            localState, lsNames.Count, string.Join(", ", lsNames.Take(10)));

                        var firstFile = lsNames.FirstOrDefault();
                        if (firstFile != null)
                        {
                            string file = localState + "\\" + firstFile;
                            int ferr = DirectoryScanner.TestFileReadable(file);
                            Log.Info("DevicePortal.Probe: FS read '{F}' => {Err} ({Desc})",
                                file, ferr, DescribeWin32(ferr));
                        }
                    }
                }
            }
        }

        private static string DescribeWin32(int err)
        {
            switch (err)
            {
                case 0: return "OK";
                case 2: return "ERROR_FILE_NOT_FOUND";
                case 3: return "ERROR_PATH_NOT_FOUND";
                case 5: return "ERROR_ACCESS_DENIED";
                case 15: return "ERROR_DRIVE_NOT_FOUND";
                case 21: return "ERROR_NOT_READY";
                case 32: return "ERROR_SHARING_VIOLATION";
                case 50: return "ERROR_NOT_SUPPORTED";
                case 53: return "ERROR_BAD_NETPATH";
                case 87: return "ERROR_INVALID_PARAMETER";
                case 123: return "ERROR_INVALID_NAME";
                case 124: return "ERROR_BAD_LENGTH";
                case 206: return "ERROR_FILENAME_EXCED_RANGE";
                default: return $"0x{err:X8}";
            }
        }

        private static void LogConnectionProfile()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile == null)
                {
                    Log.Warn("DevicePortal.Probe: no internet connection profile");
                    return;
                }
                var level = profile.GetNetworkConnectivityLevel();
                var adapter = profile.NetworkAdapter?.IanaInterfaceType ?? 0;
                Log.Info("DevicePortal.Probe: profile level={Level}, adapterType={Adapter}, isWlan={Wlan}",
                    level, adapter, profile.IsWlanConnectionProfile);
            }
            catch (Exception ex)
            {
                Log.Warn("DevicePortal.Probe: connection profile error: {Message}", ex.Message);
            }
        }

        private static async Task TestInternetAsync()
        {
            var tcpOk = await TestTcpAsync(InternetProbeHost, 80);
            if (!tcpOk)
            {
                Log.Warn("DevicePortal.Probe: internet TCP to {Host}:80 FAILED — app likely has no outbound network at all", InternetProbeHost);
                return;
            }

            try
            {
                using (var client = new HttpClient())
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                using (var resp = await client.GetAsync(new Uri(InternetProbeUrl)).AsTask(cts.Token))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    Log.Info("DevicePortal.Probe: internet HTTP {Url} => {Status} body={Body}",
                        InternetProbeUrl, (int)resp.StatusCode, body.Trim());
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DevicePortal.Probe: internet HTTP {Url} FAILED: {Message}", InternetProbeUrl, ex.Message);
            }
        }

        /// <summary>
        /// Raw TCP connect (no HTTP, no TLS) — isolates network-layer reachability
        /// from TLS/cert problems. Returns true if the connection completed.
        /// </summary>
        private static async Task<bool> TestTcpAsync(string host, int port)
        {
            var sw = Stopwatch.StartNew();
            using (var socket = new StreamSocket())
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                try
                {
                    await socket.ConnectAsync(new HostName(host), port.ToString()).AsTask(cts.Token);
                    sw.Stop();
                    Log.Info("DevicePortal.Probe: TCP {Host}:{Port} OK in {Elapsed}ms", host, port, sw.ElapsedMilliseconds);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    sw.Stop();
                    Log.Warn("DevicePortal.Probe: TCP {Host}:{Port} TIMEOUT after {Elapsed}ms", host, port, sw.ElapsedMilliseconds);
                    return false;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    var status = SocketError.GetStatus(ex.HResult);
                    Log.Warn("DevicePortal.Probe: TCP {Host}:{Port} FAILED in {Elapsed}ms: {Status} ({Message})",
                        host, port, sw.ElapsedMilliseconds, status, ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// HTTP GET against the portal, both HTTPS (with cert-ignore filter) and
        /// plain HTTP variants. Sets _baseUrl on the first success.
        /// </summary>
        private static async Task TestHttpGetAsync(string host, int port, bool useTls)
        {
            var scheme = useTls ? "https" : "http";
            var uriHost = useTls && host == "::1" ? "[::1]" : host;
            var url = $"{scheme}://{uriHost}:{port}{KnownFoldersPath}";

            Windows.Web.Http.HttpClient client;
            HttpBaseProtocolFilter filter = null;
            if (useTls)
            {
                filter = new HttpBaseProtocolFilter();
                // Dev portal uses a self-signed cert; connecting by IP also trips name validation.
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.RevocationFailure);
                client = new HttpClient(filter);
            }
            else
            {
                client = new HttpClient();
            }

            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{DevPortalSecrets.User}:{DevPortalSecrets.Password}"));
            client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Basic", auth);

            var sw = Stopwatch.StartNew();
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                using (var resp = await client.GetAsync(new Uri(url)).AsTask(cts.Token))
                {
                    sw.Stop();
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        string hostForUrl = host.Contains(":") ? $"[{host}]" : host;
                        _baseUrl = $"{scheme}://{hostForUrl}:{port}";
                        Log.Info("DevicePortal.Probe: {Url} => HTTP {Status} in {Elapsed}ms — CONNECTED. KnownFolders: {Body}",
                            url, (int)resp.StatusCode, sw.ElapsedMilliseconds, body.Trim());
                        return;
                    }
                    Log.Warn("DevicePortal.Probe: {Url} => HTTP {Status} in {Elapsed}ms",
                        url, (int)resp.StatusCode, sw.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                Log.Warn("DevicePortal.Probe: {Url} TIMEOUT after {Elapsed}ms", url, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log.Warn("DevicePortal.Probe: {Url} FAILED in {Elapsed}ms: {Message}", url, sw.ElapsedMilliseconds, ex.Message);
            }
            finally
            {
                client.Dispose();
                filter?.Dispose();
            }
        }

        /// <summary>
        /// Deep probe: once a base URL works, validate the actual portal REST endpoints
        /// used to browse another app's AppData — list packages, list LocalAppData of a
        /// non-system package, and read one small config file. This proves the full
        /// elevated path (auth + endpoint shape) works before we build the feature on it.
        /// </summary>
        private static async Task DeepProbeAsync(string baseUrl)
        {
            bool useTls = baseUrl.StartsWith("https:", StringComparison.Ordinal);
            HttpBaseProtocolFilter filter;
            var client = CreatePortalClient(useTls, out filter);
            using (client)
            using (filter)
            {
                await ProbePackagesAsync(client, baseUrl);
            }
        }

        private static Windows.Web.Http.HttpClient CreatePortalClient(bool useTls, out HttpBaseProtocolFilter filter)
        {
            filter = null;
            if (useTls)
            {
                filter = new HttpBaseProtocolFilter();
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.RevocationFailure);
            }
            var client = new HttpClient(filter);
            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{DevPortalSecrets.User}:{DevPortalSecrets.Password}"));
            client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Basic", auth);
            return client;
        }

        private static async Task ProbePackagesAsync(Windows.Web.Http.HttpClient client, string baseUrl)
        {
            var url = baseUrl + "/api/app/packagemanager/packages";
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (var resp = await client.GetAsync(new Uri(url)).AsTask(cts.Token))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        Log.Warn("DevicePortal.Probe: packages => HTTP {Status}", (int)resp.StatusCode);
                        return;
                    }

                    JsonValue val;
                    try { val = JsonValue.Parse(body); }
                    catch (Exception ex)
                    {
                        Log.Warn("DevicePortal.Probe: packages parse failed: {Message} — body: {Body}",
                            ex.Message, Truncate(body));
                        return;
                    }

                    JsonArray pkgs;
                    if (val.ValueType == JsonValueType.Array)
                    {
                        pkgs = val.GetArray();
                    }
                    else if (val.ValueType == JsonValueType.Object && val.GetObject().ContainsKey("Packages"))
                    {
                        pkgs = val.GetObject()["Packages"].GetArray();
                    }
                    else
                    {
                        Log.Warn("DevicePortal.Probe: packages response unexpected shape — body: {Body}", Truncate(body));
                        return;
                    }

                    var candidates = new List<string>();
                    foreach (var p in pkgs)
                    {
                        var o = p.GetObject();
                        var family = o.ContainsKey("PackageFamilyName") ? o["PackageFamilyName"].GetString() : "";
                        if (family.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)) continue;
                        var isFramework = o.ContainsKey("IsFramework") && o["IsFramework"].GetBoolean();
                        if (isFramework) continue;
                        var full = o.ContainsKey("PackageFullName") ? o["PackageFullName"].GetString() : "";
                        if (full.Length == 0) continue;
                        candidates.Add(full);
                    }

                    Log.Info("DevicePortal.Probe: packages => HTTP 200, {Total} total, {N} non-system: {First}",
                        pkgs.Count, candidates.Count, string.Join(", ", candidates.Take(6)));

                    foreach (var fullName in candidates.Take(2))
                        await ProbeLocalAppDataAsync(client, baseUrl, fullName);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DevicePortal.Probe: packages FAILED: {Message}", ex.Message);
            }
        }

        private static async Task ProbeLocalAppDataAsync(Windows.Web.Http.HttpClient client, string baseUrl, string packageFullName)
        {
            var url = baseUrl + "/api/filesystem/apps/files?knownfolderid=LocalAppData&packagefullname=" +
                      Uri.EscapeDataString(packageFullName);
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (var resp = await client.GetAsync(new Uri(url)).AsTask(cts.Token))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        Log.Warn("DevicePortal.Probe: LocalAppData '{Pkg}' => HTTP {Status}",
                            packageFullName, (int)resp.StatusCode);
                        return;
                    }

                    var items = new List<(string Path, string Type, long Size)>();
                    try
                    {
                        var val = JsonValue.Parse(body);
                        JsonArray arr = null;
                        if (val.ValueType == JsonValueType.Array) arr = val.GetArray();
                        else if (val.ValueType == JsonValueType.Object && val.GetObject().ContainsKey("items"))
                            arr = val.GetObject()["items"].GetArray();

                        if (arr == null)
                        {
                            Log.Warn("DevicePortal.Probe: LocalAppData '{Pkg}' unexpected shape — body: {Body}",
                                packageFullName, Truncate(body));
                        }
                        else
                        {
                            foreach (var it in arr)
                            {
                                var o = it.GetObject();
                                string path = o.ContainsKey("path") ? o["path"].GetString() : "?";
                                string type = o.ContainsKey("type") ? o["type"].GetString() : "?";
                                long size = o.ContainsKey("size") ? (long)o["size"].GetNumber() : 0;
                                items.Add((path, type, size));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("DevicePortal.Probe: LocalAppData parse failed: {Message} — body: {Body}",
                            ex.Message, Truncate(body));
                    }

                    Log.Info("DevicePortal.Probe: LocalAppData '{Pkg}' => HTTP 200, {N} items: {First}",
                        packageFullName, items.Count,
                        string.Join(", ", items.Select(i => i.Path).Take(10)));

                    var smallFile = items.FirstOrDefault(i =>
                        i.Type.Equals("File", StringComparison.OrdinalIgnoreCase) && i.Size > 0 && i.Size < 512 * 1024);
                    if (smallFile.Path != null)
                        await ProbePortalFileAsync(client, baseUrl, packageFullName, smallFile.Path, smallFile.Size);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DevicePortal.Probe: LocalAppData '{Pkg}' FAILED: {Message}", packageFullName, ex.Message);
            }
        }

        private static async Task ProbePortalFileAsync(Windows.Web.Http.HttpClient client, string baseUrl,
            string packageFullName, string relativePath, long size)
        {
            var url = baseUrl + "/api/filesystem/apps/file?knownfolderid=LocalAppData&packagefullname=" +
                      Uri.EscapeDataString(packageFullName) + "&filename=" + Uri.EscapeDataString(relativePath);
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (var resp = await client.GetAsync(new Uri(url)).AsTask(cts.Token))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        Log.Warn("DevicePortal.Probe: file '{Pkg}\\{Path}' => HTTP {Status}",
                            packageFullName, relativePath, (int)resp.StatusCode);
                        return;
                    }
                    string preview = body.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (preview.Length > 120) preview = preview.Substring(0, 120) + "…";
                    Log.Info("DevicePortal.Probe: file '{Pkg}\\{Path}' ({Size} bytes) => HTTP 200: {Preview}",
                        packageFullName, relativePath, size, preview);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DevicePortal.Probe: file '{Pkg}\\{Path}' FAILED: {Message}", packageFullName, relativePath, ex.Message);
            }
        }

        private static string Truncate(string s, int max = 600)
        {
            if (s == null) return "<null>";
            string flat = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (flat.Length <= max) return flat;
            return flat.Substring(0, max) + "…";
        }

        private static List<string> GetCandidateHosts()
        {
            var hosts = new List<string>();

            // 1. Console's own LAN IP — normal outbound traffic, most likely to work.
            foreach (var hostName in NetworkInformation.GetHostNames())
            {
                if (hostName.Type != HostNameType.Ipv4) continue;
                var ip = hostName.DisplayName;
                if (ip == "::1" || ip.StartsWith("127.", StringComparison.Ordinal)) continue;
                if (!hosts.Contains(ip)) hosts.Add(ip);
            }

            // 2. Loopback aliases (may be blocked by network isolation).
            hosts.Add("localhost");
            hosts.Add("127.0.0.1");
            hosts.Add("::1");

            return hosts;
        }

        private const uint TokenQuery = 0x0008;
        private const int TokenAppContainerSid = 32;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle, int tokenInformationClass,
            IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint GetLengthSid(IntPtr sid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("firewallapi.dll", SetLastError = true)]
        private static extern uint NetworkIsolationSetAppContainerConfig(uint dwNumPublicAppCs, IntPtr appContainerSids);

        /// <summary>
        /// Reads this process's AppContainer SID from the token (TokenAppContainerSid).
        /// </summary>
        private static uint GetAppContainerSid(out byte[] sidBytes)
        {
            sidBytes = null;
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out IntPtr hToken))
                return (uint)Marshal.GetLastWin32Error();
            try
            {
                uint len = 0;
                GetTokenInformation(hToken, TokenAppContainerSid, IntPtr.Zero, 0, out len);
                if (len == 0) return (uint)Marshal.GetLastWin32Error();

                IntPtr buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (!GetTokenInformation(hToken, TokenAppContainerSid, buf, len, out len))
                        return (uint)Marshal.GetLastWin32Error();

                    uint sidLen = GetLengthSid(buf);
                    sidBytes = new byte[sidLen];
                    Marshal.Copy(buf, sidBytes, 0, (int)sidLen);
                    return 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
        }

        /// <summary>
        /// Adds this app's own AppContainer SID to the loopback exemption list via
        /// NetworkIsolationSetAppContainerConfig (the API underneath checknetisolation).
        /// Returns the HRESULT; 0 = success.
        /// </summary>
        private static uint SelfExempt()
        {
            uint hr = GetAppContainerSid(out byte[] sidBytes);
            if (hr != 0 || sidBytes == null)
            {
                Log.Warn("DevicePortal.SelfExempt: GetAppContainerSid failed 0x{HR:X8}", hr);
                return hr;
            }

            IntPtr sidPtr = Marshal.AllocHGlobal(sidBytes.Length);
            Marshal.Copy(sidBytes, 0, sidPtr, sidBytes.Length);
            IntPtr saa = Marshal.AllocHGlobal(IntPtr.Size + 4);
            Marshal.WriteIntPtr(saa, 0, sidPtr);
            Marshal.WriteInt32(saa, IntPtr.Size, 0); // Attributes = 0

            try
            {
                return NetworkIsolationSetAppContainerConfig(1, saa);
            }
            finally
            {
                Marshal.FreeHGlobal(sidPtr);
                Marshal.FreeHGlobal(saa);
            }
        }
    }
}
