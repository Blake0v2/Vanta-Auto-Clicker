using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Vanta
{
    public sealed class UpdateCheck
    {
        public bool IsUpdateAvailable { get; private set; }
        public Version CurrentVersion { get; private set; }
        public Version LatestVersion { get; private set; }
        internal GitHubAsset InstallerAsset { get; private set; }
        internal GitHubAsset ChecksumAsset { get; private set; }

        internal UpdateCheck(Version current, Version latest, bool available, GitHubAsset installer, GitHubAsset checksum)
        {
            CurrentVersion = current;
            LatestVersion = latest;
            IsUpdateAvailable = available;
            InstallerAsset = installer;
            ChecksumAsset = checksum;
        }
    }

    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")]
        internal string TagName { get; set; }

        [DataMember(Name = "assets")]
        internal List<GitHubAsset> Assets { get; set; }
    }

    [DataContract]
    internal sealed class GitHubAsset
    {
        [DataMember(Name = "name")]
        internal string Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        internal string DownloadUrl { get; set; }

        [DataMember(Name = "digest")]
        internal string Digest { get; set; }

        [DataMember(Name = "size")]
        internal long Size { get; set; }
    }

    public sealed class UpdateService
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/Blake0v2/Vanta-Auto-Clicker/releases/latest";
        private const string InstallerName = "Vanta.Auto.Clicker.Setup.exe";
        private const string ChecksumsName = "SHA256SUMS.txt";
        private const long MaximumInstallerBytes = 100L * 1024L * 1024L;

        public static Version CurrentVersion
        {
            get { return NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        public async Task<UpdateCheck> CheckAsync()
        {
            byte[] json;
            using (WebClient client = CreateClient())
                json = await client.DownloadDataTaskAsync(new Uri(LatestReleaseUrl));

            if (json == null || json.Length == 0 || json.Length > 2 * 1024 * 1024)
                throw new InvalidOperationException("GitHub returned an invalid release response.");

            GitHubRelease release;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                using (var input = new MemoryStream(json, false))
                    release = (GitHubRelease)serializer.ReadObject(input);
            }
            catch (SerializationException)
            {
                throw new InvalidOperationException("GitHub returned release information Vanta could not read.");
            }

            Version latest;
            if (release == null || !TryParseVersion(release.TagName, out latest))
                throw new InvalidOperationException("The latest GitHub Release does not have a valid version number.");

            Version current = CurrentVersion;
            bool available = IsNewer(latest, current);
            if (!available)
                return new UpdateCheck(current, latest, false, null, null);

            GitHubAsset installer = FindAsset(release.Assets, InstallerName);
            if (installer == null)
                throw new InvalidOperationException("The latest release does not include the Vanta Setup file.");
            if (installer.Size <= 0 || installer.Size > MaximumInstallerBytes)
                throw new InvalidOperationException("The Vanta Setup file has an unexpected size.");
            ValidateReleaseAssetUrl(installer.DownloadUrl);

            GitHubAsset checksums = FindAsset(release.Assets, ChecksumsName);
            if (checksums != null) ValidateReleaseAssetUrl(checksums.DownloadUrl);
            return new UpdateCheck(current, latest, true, installer, checksums);
        }

        public async Task<string> DownloadAsync(UpdateCheck update)
        {
            if (update == null || !update.IsUpdateAvailable || update.InstallerAsset == null)
                throw new ArgumentException("No Vanta update is ready to download.", "update");

            string expectedHash = NormalizeDigest(update.InstallerAsset.Digest);
            if (expectedHash == null)
            {
                if (!String.IsNullOrWhiteSpace(update.InstallerAsset.Digest))
                    throw new InvalidOperationException("GitHub returned an invalid checksum for this release.");
                if (update.ChecksumAsset == null)
                    throw new InvalidOperationException("This release has no SHA-256 checksum, so Vanta will not install it.");

                byte[] checksumBytes;
                using (WebClient client = CreateClient())
                    checksumBytes = await client.DownloadDataTaskAsync(new Uri(update.ChecksumAsset.DownloadUrl));
                if (checksumBytes == null || checksumBytes.Length == 0 || checksumBytes.Length > 1024 * 1024)
                    throw new InvalidOperationException("The release checksum file is invalid.");
                expectedHash = ParseChecksum(Encoding.UTF8.GetString(checksumBytes), InstallerName);
                if (expectedHash == null)
                    throw new InvalidOperationException("The release checksum file does not contain a valid checksum for Vanta Setup.");
            }

            string updateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vanta Auto Clicker", "Updates");
            Directory.CreateDirectory(updateDirectory);
            string finalPath = Path.Combine(updateDirectory, "Vanta.Auto.Clicker.Setup-" + ShortVersion(update.LatestVersion) + ".exe");
            string partialPath = finalPath + ".partial";
            TryDelete(partialPath);
            TryDelete(finalPath);

            try
            {
                using (WebClient client = CreateClient())
                    await client.DownloadFileTaskAsync(new Uri(update.InstallerAsset.DownloadUrl), partialPath);

                var downloaded = new FileInfo(partialPath);
                if (!downloaded.Exists || downloaded.Length <= 0 || downloaded.Length > MaximumInstallerBytes ||
                    (update.InstallerAsset.Size > 0 && downloaded.Length != update.InstallerAsset.Size))
                    throw new InvalidOperationException("The downloaded Vanta Setup file has an unexpected size.");

                string actualHash = ComputeSha256(partialPath);
                if (!String.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded Vanta Setup file failed its SHA-256 verification.");

                VerifyInstallerMetadata(partialPath, update.LatestVersion);
                File.Move(partialPath, finalPath);
                MarkAsInternetDownload(finalPath, update.InstallerAsset.DownloadUrl);
                return finalPath;
            }
            catch
            {
                TryDelete(partialPath);
                TryDelete(finalPath);
                throw;
            }
        }

        public static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (String.IsNullOrWhiteSpace(value)) return false;
            string cleaned = value.Trim();
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned.Substring(1);
            if (cleaned.StartsWith(".", StringComparison.Ordinal)) cleaned = cleaned.Substring(1);
            Version parsed;
            if (!Version.TryParse(cleaned, out parsed) || parsed.Major < 0 || parsed.Minor < 0) return false;
            version = NormalizeVersion(parsed);
            return true;
        }

        public static bool IsNewer(Version candidate, Version current)
        {
            if (candidate == null || current == null) return false;
            return NormalizeVersion(candidate).CompareTo(NormalizeVersion(current)) > 0;
        }

        public static string NormalizeDigest(string digest)
        {
            if (String.IsNullOrWhiteSpace(digest)) return null;
            string value = digest.Trim();
            const string prefix = "sha256:";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string hash = value.Substring(prefix.Length);
            return IsSha256(hash) ? hash.ToLowerInvariant() : null;
        }

        public static string ParseChecksum(string contents, string exactFileName)
        {
            if (String.IsNullOrEmpty(contents) || String.IsNullOrEmpty(exactFileName)) return null;
            using (var reader = new StringReader(contents))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length < 66) continue;
                    string hash = line.Substring(0, 64);
                    if (!IsSha256(hash)) continue;
                    string name = line.Substring(64).TrimStart();
                    if (name.StartsWith("*", StringComparison.Ordinal)) name = name.Substring(1);
                    if (String.Equals(name, exactFileName, StringComparison.Ordinal)) return hash.ToLowerInvariant();
                }
            }
            return null;
        }

        private static WebClient CreateClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "Vanta-Auto-Clicker/" + ShortVersion(CurrentVersion);
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            client.Headers["X-GitHub-Api-Version"] = "2026-03-10";
            return client;
        }

        private static GitHubAsset FindAsset(List<GitHubAsset> assets, string exactName)
        {
            if (assets == null) return null;
            for (int index = 0; index < assets.Count; index++)
                if (assets[index] != null && String.Equals(assets[index].Name, exactName, StringComparison.Ordinal)) return assets[index];
            return null;
        }

        private static void ValidateReleaseAssetUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith("/Blake0v2/Vanta-Auto-Clicker/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("GitHub returned an unrecognized release download address.");
        }

        private static Version NormalizeVersion(Version version)
        {
            if (version == null) return new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }

        private static string ShortVersion(Version version)
        {
            Version normalized = NormalizeVersion(version);
            return normalized.Major + "." + normalized.Minor + "." + normalized.Build;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9') || (current >= 'a' && current <= 'f') || (current >= 'A' && current <= 'F'))) return false;
            }
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(input);
                var result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++) result.Append(hash[index].ToString("x2"));
                return result.ToString();
            }
        }

        private static void VerifyInstallerMetadata(string path, Version releaseVersion)
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            Version fileVersion;
            if (!String.Equals(info.ProductName, "Vanta Auto Clicker Setup", StringComparison.Ordinal) ||
                !String.Equals(info.CompanyName, "Vanta", StringComparison.Ordinal) ||
                !TryParseVersion(info.ProductVersion, out fileVersion) ||
                fileVersion.CompareTo(NormalizeVersion(releaseVersion)) != 0)
                throw new InvalidOperationException("The downloaded file does not identify itself as this Vanta release.");
        }

        private static void MarkAsInternetDownload(string path, string sourceUrl)
        {
            try
            {
                File.WriteAllText(path + ":Zone.Identifier",
                    "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + sourceUrl + "\r\n",
                    new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
