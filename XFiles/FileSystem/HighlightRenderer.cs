using System;

namespace XFiles.FileSystem
{
    public static class HighlightRenderer
    {
        public static string GetHighlightLang(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return "";
            string ext = extension.TrimStart('.').ToLowerInvariant();

            switch (ext)
            {
                case "js": case "mjs": case "cjs": return "javascript";
                case "ts": case "tsx": return "typescript";
                case "jsx": return "javascript";
                case "cs": return "csharp";
                case "rb": return "ruby";
                case "kt": case "kts": return "kotlin";
                case "rs": return "rust";
                case "sh": case "bash": case "zsh": case "fish": return "bash";
                case "ps1": case "psm1": case "psd1": return "powershell";
                case "yml": return "yaml";
                case "md": case "markdown": return "markdown";
                case "html": case "htm": case "xhtml": return "html";
                case "py": case "pyw": case "pyi": return "python";
                case "sql": return "sql";
                case "go": return "go";
                case "java": return "java";
                case "lua": return "lua";
                case "pl": case "pm": return "perl";
                case "swift": return "swift";
                case "dart": return "dart";
                case "r": return "r";
                case "css": return "css";
                case "scss": return "scss";
                case "less": return "less";
                case "xml": return "xml";
                case "json": case "jsonc": case "json5": return "json";
                case "tex": case "latex": return "latex";
                case "dockerfile": return "dockerfile";
                case "ini": case "cfg": case "conf": return "ini";
                case "toml": return "toml";
                case "c": case "h": return "c";
                case "cpp": case "cc": case "cxx": case "hpp": case "hxx": return "cpp";
                case "fs": case "fsx": case "fsi": return "fsharp";
                case "vb": return "vbnet";
                case "proto": return "protobuf";
                case "graphql": case "gql": return "graphql";
                default: return "";
            }
        }

        public static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        public static string BuildSvgHtml(string svgContent)
        {
            string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svgContent));
            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  body {{ margin:0; padding:12px; background:#0F1318; display:flex;
         align-items:center; justify-content:center; min-height:100vh; }}
  img {{ max-width:100%; max-height:100%; object-fit:contain; }}
</style>
</head>
<body>
<img src=""data:image/svg+xml;base64,{b64}"" />
</body></html>";
        }

        public static string BuildHighlightHtml(string escapedCode, string lang, string css, string js)
        {
            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  html, body {{ margin:0; padding:0; background:#0F1318; overflow-x:auto; }}
  pre {{ margin:0; padding:12px 8px; white-space:pre; overflow-x:auto;
         font-family:'Consolas','Courier New',monospace;
         font-size:12px; color:#dcdcdc; line-height:1.4;
         display:inline-block; min-width:100%; }}
  code {{ font-family:inherit; }}
</style>
<style>{css}</style>
</head>
<body>
<pre><code class=""{lang}"">{escapedCode}</code></pre>
<script>{js}</script>
<script>hljs.highlightBlock(document.querySelector('code'));</script>
</body></html>";
        }
    }
}
