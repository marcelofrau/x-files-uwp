/*
 * RetroAudio implementation - dispatches to game-music-emu, libopenmpt,
 * the Audio Overload PSF engine, or lazyusf (N64).
 */

#define RETROAUDIO_BUILD
#include "retroaudio.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <math.h>

#include <windows.h>
#include <fileapifromapp.h>

#include "../third_party/game-music-emu-0.6.5/gme/gme.h"
#include "../third_party/libopenmpt-0.8.7/libopenmpt/libopenmpt.h"
#include "../third_party/zlib-1.3.1/zlib.h"

/* aosdk headers lack extern "C" guards (lazyusf has its own). */
extern "C" {
#include "../third_party/aosdk_psf/ao.h"
#include "../third_party/aosdk_psf/corlett.h"
#include "../third_party/aosdk_psf/eng_protos.h"
}
#include "../third_party/lazyusf/usf.h"

/* ------------------------------------------------------------------ */
/* Session serialization                                               */
/* ------------------------------------------------------------------ */

/* The aosdk PSF engine (and lazyusf boot) reads uninitialized heap
   memory while it renders - the content of that memory is whatever the
   process's previous allocation activity left behind. When a second RA
   handle is alive in the same process (e.g. a next-track prefetch
   rendering while the current track streams), the leftover heap data is
   the OTHER track's fresh PCM, which mixes into the render as additive
   white noise (measured -40dB on desktop, audible on Xbox). Two handles
   corrupt each other even when their calls are serialized, because the
   pollution is persistent state, not a call race. GME is immune (fully
   per-instance state); PSF is corrupted by ANY concurrent session.
   Fix: hold ONE process-wide lock for the whole session (RA_Open..RA_Free)
   so only a single emulator instance is ever live at a time. SRWLOCK
   has a static initializer (CRITICAL_SECTION does not). */
static SRWLOCK g_raSessLock = SRWLOCK_INIT;

static void ra_sess_acquire(void)
{
    AcquireSRWLockExclusive(&g_raSessLock);
}

static void ra_sess_release(void)
{
    ReleaseSRWLockExclusive(&g_raSessLock);
}

/* ------------------------------------------------------------------ */
/* Format routing tables                                               */
/* ------------------------------------------------------------------ */

/* Console formats handled by game-music-emu. VGZ is a gzipped VGM -
   decompressed by Mem_File_Reader when built with HAVE_ZLIB_H. */
static const char* const kGmeExts[] = {
    "spc", "gbs", "nsf", "nsfe", "vgm", "vgz", "gym",
    "sid", "hes", "kss", "ay", "sap", NULL
};

/* Tracker formats handled by libopenmpt. */
static const char* const kOpenmptExts[] = {
    "mod", "xm", "s3m", "it", "mtm", "stm", "669", "med",
    "far", "mdl", "ult", "ptm", "dbm", "dsm", "amf", "okt",
    "dmf", "ams", "mt2", "pol", "ppm", "cba", "psm", "j2b",
    "mpm", "umx", "mo3", NULL
};

/* PlayStation Sound Format - aosdk engine_psf (mono, up-mixed to stereo). */
static const char* const kPsfExts[] = {
    "psf", "minipsf", NULL
};

/* Nintendo 64 USF - lazyusf. */
static const char* const kUsfExts[] = {
    "usf", "miniusf", NULL
};

static int extInList(const char* const* list, const char* ext)
{
    for (int i = 0; list[i] != NULL; ++i)
        if (strcmp(list[i], ext) == 0)
            return 1;
    return 0;
}

static void lowerExt(char* out, size_t outSize, const char* ext)
{
    if (outSize == 0) return;
    size_t i = 0;
    while (ext[i] != '\0' && i + 1 < outSize)
    {
        char c = ext[i];
        if (c >= 'A' && c <= 'Z') c = (char)(c - 'A' + 'a');
        out[i] = c;
        ++i;
    }
    out[i] = '\0';
}

/* ------------------------------------------------------------------ */
/* Handle                                                              */
/* ------------------------------------------------------------------ */

#define BACKEND_NONE 0
#define BACKEND_GME 1
#define BACKEND_OPENMPT 2
#define BACKEND_PSF 3
#define BACKEND_USF 4

struct RA_Handle
{
    int backend;
    int sampleRate;
    int channels;

    /* Streaming render state (RA_BeginTrack / RA_RenderFrames) */
    int trackTotalFrames;
    int trackRenderedFrames;

    /* GME backend */
    Music_Emu* gme;

    /* libopenmpt backend */
    openmpt_module* mod;

    /* PSF backend (aosdk engine_psf) */
    void* psf;
    long psfLengthMs;
    long psfFadeMs;

    /* USF backend (lazyusf) */
    void* usf;
    int usfRate;
    long usfLengthMs;
    long usfFadeMs;
    char usfTitle[256];
};

static double clampDuration(double sec)
{
    if (sec <= 0.0 || !isfinite(sec)) sec = 180.0; /* default for unknown length */
    if (sec > RA_MAX_SECONDS) sec = RA_MAX_SECONDS;
    return sec;
}

/* ------------------------------------------------------------------ */
/* PSF/USF shared helpers                                              */
/* ------------------------------------------------------------------ */

/* Read a whole file through the UWP app-capability file APIs (the same
   FromApp pattern the rest of X-Files uses; plain fopen is blocked on Xbox). */
static int ra_read_file_fromapp(const char* utf8Path, void** outData, uint64_t* outSize)
{
    *outData = NULL;
    *outSize = 0;
    wchar_t wide[MAX_PATH];
    if (MultiByteToWideChar(CP_UTF8, 0, utf8Path, -1, wide, MAX_PATH) == 0)
        return 0;
    /* CreateFile2FromAppW is exported by kernel32 on Xbox/UWP and Win10 desktop
       in most builds, but is absent from some desktop kernels (proc addr 0).
       Fall back to plain CreateFileW — desktop equivalent. On Xbox the FromApp
       variant exists, so UWP stays covered. */
    typedef HANDLE(WINAPI* CreateFile2FromAppProc)(PCWSTR, DWORD, DWORD, DWORD, void*);
    static CreateFile2FromAppProc pCreateFile2FromApp = NULL;
    static int s_createFile2Resolved = 0;
    if (!s_createFile2Resolved)
    {
        HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
        if (k32 != NULL)
            pCreateFile2FromApp = (CreateFile2FromAppProc)GetProcAddress(k32, "CreateFile2FromAppW");
        s_createFile2Resolved = 1;
    }
    HANDLE h = INVALID_HANDLE_VALUE;
    if (pCreateFile2FromApp != NULL)
    {
        h = pCreateFile2FromApp(wide, GENERIC_READ, FILE_SHARE_READ,
                                OPEN_EXISTING, NULL);
    }
    else
    {
        h = CreateFileW(wide, GENERIC_READ, FILE_SHARE_READ, NULL,
                        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    }
    if (h == INVALID_HANDLE_VALUE)
        return 0;
    LARGE_INTEGER sz;
    if (!GetFileSizeEx(h, &sz))
    {
        CloseHandle(h);
        return 0;
    }
    if (sz.QuadPart <= 0 || sz.QuadPart > (LONGLONG)RA_MAX_SECONDS * 44100 * 4)
    {
        CloseHandle(h);
        return 0;
    }
    uint8_t* buf = (uint8_t*)malloc((size_t)sz.QuadPart + 1);
    if (buf == NULL)
    {
        CloseHandle(h);
        return 0;
    }
    DWORD total = 0;
    while (total < (DWORD)sz.QuadPart)
    {
        DWORD rd = 0;
        if (!ReadFile(h, buf + total, (DWORD)(sz.QuadPart - total), &rd, NULL) || rd == 0)
            break;
        total += rd;
    }
    CloseHandle(h);
    if (total != (DWORD)sz.QuadPart)
    {
        free(buf);
        return 0;
    }
    buf[sz.QuadPart] = '\0';
    *outData = buf;
    *outSize = total;
    return 1;
}

/* Host callbacks for the aosdk PSF engine (declared in ao.h). The engine
   resolves "psflib" tags by calling ao_getlibpath() then ao_get_lib(). */
extern "C" void ao_getlibpath(const char* path, const char* libname,
                              char* libpath, int size)
{
    if (libname == NULL || libname[0] == '\0' || libpath == NULL || size <= 0)
        return;
    /* _lib tags from real rips (Zophar/PSF-Central) carry the full filename
       WITH extension (e.g. "ct.psflib"); some older demo rips are
       extension-less. Only append ".psflib" when the name has no extension,
       otherwise "<base>\<lib>.psflib" double-extends and the open fails. */
    if (strchr(libname, '.') != NULL)
        _snprintf(libpath, (size_t)size, "%s\\%s",
                  (path != NULL && path[0] != '\0') ? path : ".", libname);
    else
        _snprintf(libpath, (size_t)size, "%s\\%s.psflib",
                  (path != NULL && path[0] != '\0') ? path : ".", libname);
}

extern "C" int ao_get_lib(char* filename, uint8** buffer, uint64* length)
{
    if (filename == NULL || buffer == NULL || length == NULL)
        return AO_FAIL;
    *buffer = NULL;
    *length = 0;
    if (filename[0] == '\0')
        return AO_FAIL;

    /* psflib tags may list fallback candidates separated by '?'. */
    const char* cur = filename;
    while (cur != NULL && *cur != '\0')
    {
        const char* q = strchr(cur, '?');
        size_t n = (q != NULL) ? (size_t)(q - cur) : strlen(cur);
        if (n > 0)
        {
            char candidate[MAX_PATH];
            if (n >= sizeof(candidate)) n = sizeof(candidate) - 1;
            memcpy(candidate, cur, n);
            candidate[n] = '\0';
            void* data = NULL;
            uint64_t size = 0;
            if (ra_read_file_fromapp(candidate, &data, &size))
            {
                *buffer = (uint8*)data;
                *length = size;
                return AO_SUCCESS;
            }
        }
        if (q == NULL) break;
        cur = q + 1;
    }
    return AO_FAIL;
}

/* PSF container (shared by USF parsing): header, optional zlib program,
   and the trailing "key=value" tags. */
typedef struct
{
    uint8_t* reserved;
    size_t reservedSize;
    uint8_t* program;
    size_t programSize;
    char libName[256];   /* _lib / usflib tag (full filename) */
    char length[32];
    char fade[32];
    char title[256];
    int hasCompare;
    int hasFifoFull;
} RA_PsfFile;

static uint32_t ra_le32(const uint8_t* p)
{
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static void ra_psf_free(RA_PsfFile* f)
{
    if (f->reserved != NULL) free(f->reserved);
    if (f->program != NULL) free(f->program);
    f->reserved = NULL;
    f->program = NULL;
}

static int ra_psf_parse(const uint8_t* data, size_t size, RA_PsfFile* out)
{
    memset(out, 0, sizeof(*out));
    if (size < 16 || data[0] != 'P' || data[1] != 'S' || data[2] != 'F')
        return 0;
    uint32_t resSize = ra_le32(data + 4);
    uint32_t progSize = ra_le32(data + 8);
    if (16ULL + resSize + progSize > size)
        return 0;

    /* reserved area is stored raw */
    if (resSize > 0)
    {
        out->reserved = (uint8_t*)malloc(resSize);
        if (out->reserved == NULL) return 0;
        memcpy(out->reserved, data + 16, resSize);
        out->reservedSize = resSize;
    }

    /* program area is zlib-compressed when non-empty (Corlett convention) */
    if (progSize > 0)
    {
        const uint8_t* progSrc = data + 16 + resSize;
        uint8_t* decomp = (uint8_t*)malloc(progSize + 1);
        uLongf outLen = progSize + 1;
        if (decomp == NULL) return 0;
        if (uncompress(decomp, &outLen, progSrc, progSize) == Z_OK)
        {
            out->program = decomp;
            out->programSize = (size_t)outLen;
        }
        else
        {
            free(decomp);
            return 0;
        }
    }

    /* trailing tags */
    size_t off = 16 + (size_t)resSize + (size_t)progSize;
    if (size > off + 5 && memcmp(data + off, "[TAG]", 5) == 0)
    {
        off += 5;
        const char* p = (const char*)data + off;
        const char* end = (const char*)data + size;
        while (p < end && *p != '\0')
        {
            const char* nl = (const char*)memchr(p, '\n', (size_t)(end - p));
            const char* lineEnd = (nl != NULL) ? nl : end;
            const char* eq = (const char*)memchr(p, '=', (size_t)(lineEnd - p));
            if (eq != NULL)
            {
                size_t nameLen = (size_t)(eq - p);
                size_t valLen = (size_t)(lineEnd - eq - 1);
                char name[64] = { 0 };
                char value[256] = { 0 };
                if (nameLen < sizeof(name)) memcpy(name, p, nameLen);
                if (valLen < sizeof(value)) memcpy(value, eq + 1, valLen);
                if (name[0] != '\0' && value[0] != '\0')
                {
                    if (strcmp(name, "_lib") == 0 || strcmp(name, "usflib") == 0)
                        _snprintf(out->libName, sizeof(out->libName), "%s", value);
                    else if (strcmp(name, "length") == 0)
                        _snprintf(out->length, sizeof(out->length), "%s", value);
                    else if (strcmp(name, "fade") == 0)
                        _snprintf(out->fade, sizeof(out->fade), "%s", value);
                    else if (strcmp(name, "title") == 0)
                        _snprintf(out->title, sizeof(out->title), "%s", value);
                    else if (strcmp(name, "compare") == 0)
                        out->hasCompare = 1;
                    else if (strcmp(name, "fifofull") == 0)
                        out->hasFifoFull = 1;
                }
            }
            p = (nl != NULL && nl < end) ? nl + 1 : end;
        }
    }
    return 1;
}

static long ra_tag_to_ms(const char* s, long fallbackMs)
{
    if (s == NULL || s[0] == '\0' || strcmp(s, "n/a") == 0)
        return fallbackMs;
    long ms = psfTimeToMS((char*)s);
    return (ms > 0) ? ms : fallbackMs;
}

/* ------------------------------------------------------------------ */
/* Backend openers                                                     */
/* ------------------------------------------------------------------ */

static int ra_open_psf(const uint8_t* data, size_t size, const char* baseDir,
                       RA_Handle* h)
{
    void* ph = psf_start(baseDir, (uint8_t*)data, (uint32_t)size);
    if (ph == NULL)
        return RA_ERR_OPEN;
    h->psf = ph;

    ao_display_info info;
    memset(&info, 0, sizeof(info));
    if (psf_fill_info(ph, &info) == AO_SUCCESS)
    {
        h->psfLengthMs = psfTimeToMS(info.info[6]);
        h->psfFadeMs = psfTimeToMS(info.info[7]);
    }
    return RA_OK;
}

static int ra_open_usf(const uint8_t* data, size_t size, const char* baseDir,
                       RA_Handle* h)
{
    RA_PsfFile main;
    if (!ra_psf_parse(data, size, &main))
    {
        ra_psf_free(&main);
        return RA_ERR_OPEN;
    }

    void* state = malloc(usf_get_state_size());
    if (state == NULL)
    {
        ra_psf_free(&main);
        return RA_ERR_NOMEM;
    }
    usf_clear(state);

    /* nested library first (deepest), then the main file */
    if (main.libName[0] != '\0')
    {
        char libPath[MAX_PATH];
        _snprintf(libPath, sizeof(libPath), "%s\\%s", baseDir, main.libName);
        void* libData = NULL;
        uint64_t libSize = 0;
        if (ra_read_file_fromapp(libPath, &libData, &libSize))
        {
            RA_PsfFile lib;
            if (ra_psf_parse((const uint8_t*)libData, (size_t)libSize, &lib))
            {
                if (lib.reservedSize > 0)
                    usf_upload_section(state, lib.reserved, lib.reservedSize);
                if (lib.programSize > 0)
                    usf_upload_section(state, lib.program, lib.programSize);
                ra_psf_free(&lib);
            }
            free(libData);
        }
    }

    if (main.reservedSize > 0)
        usf_upload_section(state, main.reserved, main.reservedSize);
    if (main.programSize > 0)
        usf_upload_section(state, main.program, main.programSize);

    if (main.hasCompare) usf_set_compare(state, 1);
    if (main.hasFifoFull) usf_set_fifo_full(state, 1);

    int32_t rate = 0;
    const char* err = usf_render(state, NULL, 0, &rate);
    if (err != NULL || rate <= 0)
    {
        usf_shutdown(state);
        free(state);
        ra_psf_free(&main);
        return RA_ERR_OPEN;
    }

    h->usf = state;
    h->usfRate = (int)rate;
    h->sampleRate = (int)rate; /* N64 AI output rate (usually 44100) */
    h->usfLengthMs = ra_tag_to_ms(main.length, 0);
    h->usfFadeMs = ra_tag_to_ms(main.fade, 0);
    _snprintf(h->usfTitle, sizeof(h->usfTitle), "%s", main.title);
    ra_psf_free(&main);
    return RA_OK;
}

/* ------------------------------------------------------------------ */
/* Exported API                                                        */
/* ------------------------------------------------------------------ */

RA_API const char* RA_GetVersion(void)
{
    return "retroaudio 1.1 (gme 0.6.5, libopenmpt 0.8.7, aosdk psf, lazyusf 1.2)";
}

RA_API int RA_IsSupportedExt(const char* ext)
{
    if (ext == NULL) return 0;
    char e[16];
    lowerExt(e, sizeof(e), ext);
    return extInList(kGmeExts, e) || extInList(kOpenmptExts, e) ||
           extInList(kPsfExts, e) || extInList(kUsfExts, e);
}

RA_API int RA_Open(const void* data, size_t size, const char* ext,
                   const char* baseDir, RA_Handle** outHandle)
{
    if (outHandle == NULL) return RA_ERR_INTERNAL;
    *outHandle = NULL;
    if (data == NULL || size == 0) return RA_ERR_OPEN;

    ra_sess_acquire();

    char e[16] = "";
    if (ext != NULL) lowerExt(e, sizeof(e), ext);

    RA_Handle* h = (RA_Handle*)calloc(1, sizeof(RA_Handle));
    if (h == NULL)
    {
        ra_sess_release();
        return RA_ERR_NOMEM;
    }
    h->sampleRate = RA_SAMPLE_RATE;
    h->channels = RA_CHANNELS;

    if (extInList(kGmeExts, e))
    {
        Music_Emu* gme = NULL;
        const char* err = gme_open_data(data, (long)size, &gme, h->sampleRate);
        if (err != NULL || gme == NULL)
        {
            free(h);
            ra_sess_release();
            return RA_ERR_OPEN;
        }
        h->backend = BACKEND_GME;
        h->gme = gme;
        /* Disable Music_Emu silence-detection zero-padding: with the default
           (ignore_silence_ = false) the fork's play() runs count_silence +
           fill_buf at the end of every call, which pre-renders ahead and pads
           the start of the next gme_play() call with silence. With the 8192-
           sample render chunk this injects one ~60-90ms gap per chunk (161 in
           the first 30s) into rendered WAVs - audible rhythmic stutter on
           SPC/GB (openmpt has no such logic). gme_ignore_silence() skips that
           block during normal playback so audio renders continuously. */
        gme_ignore_silence(gme, 1);
    }
    else if (extInList(kOpenmptExts, e))
    {
        int errCode = 0;
        const char* errMsg = NULL;
        openmpt_module* mod = openmpt_module_create_from_memory2(
            data, size,
            NULL, NULL,   /* logfunc, loguser */
            NULL, NULL,   /* errfunc, erruser */
            &errCode, &errMsg,
            NULL);        /* ctls */
        if (mod == NULL)
        {
            free(h);
            ra_sess_release();
            return RA_ERR_OPEN;
        }
        openmpt_module_set_repeat_count(mod, 0); /* play through once */
        h->backend = BACKEND_OPENMPT;
        h->mod = mod;
    }
    else if (extInList(kPsfExts, e))
    {
        h->backend = BACKEND_PSF;
        int rc = ra_open_psf((const uint8_t*)data, size, baseDir, h);
        if (rc != RA_OK)
        {
            free(h);
            ra_sess_release();
            return rc;
        }
    }
    else if (extInList(kUsfExts, e))
    {
        h->backend = BACKEND_USF;
        int rc = ra_open_usf((const uint8_t*)data, size, baseDir, h);
        if (rc != RA_OK)
        {
            free(h);
            ra_sess_release();
            return rc;
        }
    }
    else
    {
        free(h);
        ra_sess_release();
        return RA_ERR_FORMAT;
    }

    *outHandle = h;
    return RA_OK;
}

RA_API void RA_Free(RA_Handle* h)
{
    if (h == NULL) return;
    if (h->backend == BACKEND_GME && h->gme != NULL)
        gme_delete(h->gme);
    if (h->backend == BACKEND_OPENMPT && h->mod != NULL)
        openmpt_module_destroy(h->mod);
    if (h->backend == BACKEND_PSF && h->psf != NULL)
        psf_stop(h->psf);
    if (h->backend == BACKEND_USF && h->usf != NULL)
    {
        usf_shutdown(h->usf);
        free(h->usf);
    }
    free(h);
    ra_sess_release();
}

RA_API int RA_GetSampleRate(RA_Handle* h)
{
    return h ? h->sampleRate : 0;
}

RA_API int RA_GetChannels(RA_Handle* h)
{
    return h ? h->channels : 0;
}

RA_API int RA_GetTrackCount(RA_Handle* h)
{
    if (h == NULL) return 0;
    if (h->backend == BACKEND_GME)
        return gme_track_count(h->gme);
    if (h->backend == BACKEND_OPENMPT)
        return openmpt_module_get_num_subsongs(h->mod);
    if (h->backend == BACKEND_PSF || h->backend == BACKEND_USF)
        return 1;
    return 0;
}

RA_API double RA_GetDurationSec(RA_Handle* h, int track)
{
    if (h == NULL) return 0.0;
    if (h->backend == BACKEND_GME)
    {
        gme_info_t* info = NULL;
        const char* err = gme_track_info(h->gme, &info, track);
        if (err != NULL || info == NULL) return 0.0;
        double ms = (info->length > 0) ? (double)info->length : 150000.0;
        if (info->fade_length > 0) ms += (double)info->fade_length;
        gme_free_info(info);
        return clampDuration(ms / 1000.0);
    }
    if (h->backend == BACKEND_OPENMPT)
    {
        openmpt_module_set_repeat_count(h->mod, 0);
        double sec = openmpt_module_get_duration_seconds(h->mod);
        return clampDuration(sec);
    }
    if (h->backend == BACKEND_PSF)
        return clampDuration((double)(h->psfLengthMs + h->psfFadeMs) / 1000.0);
    if (h->backend == BACKEND_USF)
        return clampDuration((double)(h->usfLengthMs + h->usfFadeMs) / 1000.0);
    return 0.0;
}

RA_API int RA_GetTrackTitle(RA_Handle* h, int track, char* out, size_t outSize)
{
    if (h == NULL || out == NULL || outSize == 0) return 0;
    out[0] = '\0';

    if (h->backend == BACKEND_GME)
    {
        gme_info_t* info = NULL;
        const char* err = gme_track_info(h->gme, &info, track);
        if (err != NULL || info == NULL) return 0;
        const char* title = (info->song != NULL && info->song[0] != '\0')
            ? info->song
            : (info->game != NULL ? info->game : NULL);
        if (title != NULL)
        {
            size_t n = strlen(title);
            if (n >= outSize) n = outSize - 1;
            memcpy(out, title, n);
            out[n] = '\0';
        }
        gme_free_info(info);
    }
    else if (h->backend == BACKEND_OPENMPT)
    {
        const char* name = openmpt_module_get_subsong_name(h->mod, track);
        if (name != NULL && name[0] != '\0')
        {
            size_t n = strlen(name);
            if (n >= outSize) n = outSize - 1;
            memcpy(out, name, n);
            out[n] = '\0';
        }
    }
    else if (h->backend == BACKEND_PSF && h->psf != NULL)
    {
        ao_display_info info;
        memset(&info, 0, sizeof(info));
        if (psf_fill_info(h->psf, &info) == AO_SUCCESS && info.info[1][0] != '\0')
        {
            size_t n = strlen(info.info[1]);
            if (n >= outSize) n = outSize - 1;
            memcpy(out, info.info[1], n);
            out[n] = '\0';
        }
    }
    else if (h->backend == BACKEND_USF && h->usfTitle[0] != '\0')
    {
        size_t n = strlen(h->usfTitle);
        if (n >= outSize) n = outSize - 1;
        memcpy(out, h->usfTitle, n);
        out[n] = '\0';
    }
    return (int)strlen(out);
}

RA_API int RA_Render(RA_Handle* h, int track, int16_t* pcm, int capacityFrames,
                     int* framesWritten, double* durationSec)
{
    if (h == NULL || pcm == NULL || framesWritten == NULL) return RA_ERR_INTERNAL;
    *framesWritten = 0;
    if (durationSec != NULL) *durationSec = 0.0;

    int totalFrames = (int)(RA_GetDurationSec(h, track) * h->sampleRate);
    if (totalFrames > capacityFrames) totalFrames = capacityFrames;
    if (totalFrames <= 0) return RA_OK;

    if (h->backend == BACKEND_GME)
    {
        const char* err = gme_start_track(h->gme, track);
        if (err != NULL) return RA_ERR_TRACK;

        int frames = 0;
        const int chunk = 8192;
        while (frames < totalFrames)
        {
            int n = totalFrames - frames;
            if (n > chunk) n = chunk;
            err = gme_play(h->gme, n, pcm + frames * 2);
            if (err != NULL) return RA_ERR_INTERNAL;
            frames += n;
            if (gme_track_ended(h->gme)) break;
        }
        *framesWritten = frames;
        if (durationSec != NULL) *durationSec = (double)frames / h->sampleRate;
        return RA_OK;
    }

    if (h->backend == BACKEND_OPENMPT)
    {
        if (track < 0 || track >= RA_GetTrackCount(h)) return RA_ERR_TRACK;
        openmpt_module_set_repeat_count(h->mod, 0);
        openmpt_module_select_subsong(h->mod, track);

        int frames = 0;
        while (frames < totalFrames)
        {
            int n = totalFrames - frames;
            if (n > 8192) n = 8192;
            int got = openmpt_module_read_interleaved_stereo(
                h->mod, h->sampleRate, n, pcm + frames * 2);
            if (got <= 0) break;
            frames += got;
        }
        *framesWritten = frames;
        if (durationSec != NULL) *durationSec = (double)frames / h->sampleRate;
        return RA_OK;
    }

    if (h->backend == BACKEND_PSF)
    {
        if (psf_command(h->psf, COMMAND_RESTART, 0) != AO_SUCCESS) return RA_ERR_TRACK;
        int frames = 0;
        /* psf_gen outputs STEREO interleaved (P.E.Op.S SPU mixes L/R) even though
           the engine calls it "mono" — a mono buffer underflows the flush size and
           corrupts the heap (STATUS_HEAP_CORRUPTION). Allocate stereo + margin. */
        int16_t* tmp = (int16_t*)calloc(8192 + 1024, 4 * sizeof(int16_t));
        if (tmp == NULL) return RA_ERR_NOMEM;
        while (frames < totalFrames)
        {
            int n = totalFrames - frames;
            if (n > 8192) n = 8192;
            psf_gen(h->psf, tmp, (uint32_t)n);
            memcpy(pcm + frames * 2, tmp, (size_t)n * 2 * sizeof(int16_t));
            frames += n;
        }
        free(tmp);
        *framesWritten = frames;
        if (durationSec != NULL) *durationSec = (double)frames / h->sampleRate;
        return RA_OK;
    }

    if (h->backend == BACKEND_USF)
    {
        usf_restart(h->usf);
        int frames = 0;
        while (frames < totalFrames)
        {
            int n = totalFrames - frames;
            if (n > 8192) n = 8192;
            int32_t rate = h->usfRate;
            const char* err = usf_render(h->usf, pcm + frames * 2, n, &rate);
            if (err != NULL) break;
            h->usfRate = (int)rate;
            frames += n;
        }
        *framesWritten = frames;
        if (durationSec != NULL) *durationSec = (double)frames / h->sampleRate;
        return RA_OK;
    }

    return RA_ERR_INTERNAL;
}

RA_API int RA_BeginTrack(RA_Handle* h, int track)
{
    if (h == NULL) return RA_ERR_INTERNAL;
    h->trackTotalFrames = 0;
    h->trackRenderedFrames = 0;

    double sec = RA_GetDurationSec(h, track);
    if (sec > 0.0)
        h->trackTotalFrames = (int)(sec * h->sampleRate);

    if (h->backend == BACKEND_GME)
    {
        if (track < 0 || track >= gme_track_count(h->gme)) return RA_ERR_TRACK;
        const char* err = gme_start_track(h->gme, track);
        return (err != NULL) ? RA_ERR_TRACK : RA_OK;
    }
    if (h->backend == BACKEND_OPENMPT)
    {
        if (track < 0 || track >= openmpt_module_get_num_subsongs(h->mod)) return RA_ERR_TRACK;
        openmpt_module_set_repeat_count(h->mod, 0);
        openmpt_module_select_subsong(h->mod, track);
        return RA_OK;
    }
    if (h->backend == BACKEND_PSF)
    {
        if (track != 0) return RA_ERR_TRACK;
        if (psf_command(h->psf, COMMAND_RESTART, 0) != AO_SUCCESS) return RA_ERR_TRACK;
        return RA_OK;
    }
    if (h->backend == BACKEND_USF)
    {
        if (track != 0) return RA_ERR_TRACK;
        usf_restart(h->usf);
        return RA_OK;
    }
    return RA_ERR_INTERNAL;
}

RA_API int RA_RenderFrames(RA_Handle* h, int16_t* pcm, int capacityFrames,
                           int* framesWritten, int* trackEnded)
{
    if (h == NULL || pcm == NULL || framesWritten == NULL || trackEnded == NULL)
        return RA_ERR_INTERNAL;
    *framesWritten = 0;
    *trackEnded = 0;
    if (capacityFrames <= 0) return RA_ERR_INTERNAL;

    int remaining = h->trackTotalFrames - h->trackRenderedFrames;
    if (h->trackTotalFrames > 0 && remaining <= 0)
    {
        *trackEnded = 1; /* duration cap reached */
        return RA_OK;
    }

    if (h->backend == BACKEND_GME)
    {
        /* gme_play() count is in STEREO SAMPLES, not frames — multiply by 2.
           Passing frames directly made GME render half the chunk and left the
           rest as zeros (audible stutter in SPC/GBS/NES/VGM renders). */
        int n = capacityFrames * 2;
        if (n > 8192 * 2) n = 8192 * 2;
        if (remaining > 0 && n > remaining * 2) n = remaining * 2;
        const char* err = gme_play(h->gme, n, pcm);
        if (err != NULL) return RA_ERR_INTERNAL;
        h->trackRenderedFrames += n / 2;
        *framesWritten = n / 2;
        *trackEnded = gme_track_ended(h->gme) ? 1 : 0;
        if (h->trackTotalFrames > 0 && h->trackRenderedFrames >= h->trackTotalFrames)
            *trackEnded = 1;
        return RA_OK;
    }

    if (h->backend == BACKEND_OPENMPT)
    {
        int n = capacityFrames;
        if (n > 8192) n = 8192;
        if (remaining > 0 && n > remaining) n = remaining;
        int got = openmpt_module_read_interleaved_stereo(
            h->mod, h->sampleRate, n, pcm);
        if (got < 0) return RA_ERR_INTERNAL;
        h->trackRenderedFrames += got;
        *framesWritten = got;
        *trackEnded = (got == 0) ? 1 : 0;
        if (h->trackTotalFrames > 0 && h->trackRenderedFrames >= h->trackTotalFrames)
            *trackEnded = 1;
        return RA_OK;
    }

    if (h->backend == BACKEND_PSF)
    {
        int n = capacityFrames;
        if (n > 8192) n = 8192;
        if (remaining > 0 && n > remaining) n = remaining;
        /* psf_gen outputs STEREO interleaved — buffer must hold 2*n int16s, else
           the SPU flush overflows the heap (STATUS_HEAP_CORRUPTION). */
        int16_t* tmp = (int16_t*)calloc(n + 1024, 4 * sizeof(int16_t));
        if (tmp == NULL) return RA_ERR_NOMEM;
        psf_gen(h->psf, tmp, (uint32_t)n);
        memcpy(pcm, tmp, (size_t)n * 2 * sizeof(int16_t));
        free(tmp);
        h->trackRenderedFrames += n;
        *framesWritten = n;
        if (h->trackTotalFrames > 0 && h->trackRenderedFrames >= h->trackTotalFrames)
            *trackEnded = 1;
        return RA_OK;
    }

    if (h->backend == BACKEND_USF)
    {
        int n = capacityFrames;
        if (n > 8192) n = 8192;
        if (remaining > 0 && n > remaining) n = remaining;
        int32_t rate = h->usfRate;
        const char* err = usf_render(h->usf, pcm, n, &rate);
        h->usfRate = (int)rate;
        if (err != NULL)
        {
            /* emulator signaled end — zero-fill what it did not produce */
            memset(pcm, 0, (size_t)n * 2 * sizeof(int16_t));
            h->trackRenderedFrames += n;
            *framesWritten = n;
            *trackEnded = 1;
            return RA_OK;
        }
        h->trackRenderedFrames += n;
        *framesWritten = n;
        if (h->trackTotalFrames > 0 && h->trackRenderedFrames >= h->trackTotalFrames)
            *trackEnded = 1;
        return RA_OK;
    }

    return RA_ERR_INTERNAL;
}

RA_API void RA_EndTrack(RA_Handle* h)
{
    (void)h; /* no per-track state to release; next RA_BeginTrack resets */
}
