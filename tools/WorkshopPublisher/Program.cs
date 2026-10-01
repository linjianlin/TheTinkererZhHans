using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Steamworks;

internal static class Program
{
    const uint AppId = 2868840;
    const int TimeoutSeconds = 120;
    static readonly string ToolDir = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "ToolDirectory").Value!;
    static readonly string StatePath = Path.Combine(ToolDir, "state.json");
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    static int Main(string[] args)
    {
        bool initialized = false;
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Check(args is ["--status"] or ["--inspect", _] or ["--validate", _] or ["--publish", _, "--confirm"] or ["--install", _, "--confirm"], "Use --status | --inspect <id> | --validate <config> | --publish <config> --confirm | --install <config> --confirm");
            string mode = args[0];
            string? configPath = mode is "--validate" or "--publish" or "--install" ? Path.GetFullPath(args[1]) : null;
            Config? config = configPath is null ? null : Validate(configPath);
            if (mode == "--validate") { Dump(new { valid = true, config, statePath = StatePath }); return 0; }
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString(), EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString(), EnvironmentVariableTarget.Process);
            Directory.SetCurrentDirectory(ToolDir);
            Check(initialized = SteamAPI.Init(), "SteamAPI.Init returned false.");
            Check(SteamUtils.GetAppID().m_AppId == AppId, "Unexpected Steam AppID.");
            if (mode == "--status") Dump(new { init = true, loggedOn = SteamUser.BLoggedOn(), steamId = SteamUser.GetSteamID().ToString(), personaName = SteamFriends.GetPersonaName(), subscribed = SteamApps.BIsSubscribed(), appId = AppId });
            else if (mode == "--inspect") Inspect(new PublishedFileId_t(Id(args[1])), out _);
            else if (mode == "--install") Install(config!, configPath!);
            else Publish(config!, configPath!);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("ERROR: " + e.Message); return 1; }
        finally { if (initialized) SteamAPI.Shutdown(); }
    }

    static Config Validate(string path)
    {
        var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty config.");
        Check(c.AppId == AppId, "appId must be 2868840.");
        Text(c.Title, 128, "title"); Visibility(c.Visibility);
        Check(c.DependencyIds.Length == 2 && c.DependencyIds.Order().SequenceEqual(new[] { "3737335127", "3747571123" }), "Only The Tinkerer and BaseLib dependencies are supported.");
        if (c.ItemId is not null) { Id(c.ItemId); Check(!c.DependencyIds.Contains(c.ItemId), "Cannot update a dependency."); }
        Id(c.ExpectedSteamId);
        string Resolve(string value) { Check(!string.IsNullOrWhiteSpace(value), "Missing path."); return Path.GetFullPath(value, Path.GetDirectoryName(path)!); }
        c.DescriptionFile = Resolve(c.DescriptionFile); c.ContentDirectory = Resolve(c.ContentDirectory); c.PreviewFile = Resolve(c.PreviewFile);
        c.Description = File.ReadAllText(c.DescriptionFile, new UTF8Encoding(false, true)); Text(c.Description, 7999, "description");
        Check(!c.ChangeNote.Contains('\0') && Encoding.UTF8.GetByteCount(c.ChangeNote) <= 7999, "Invalid changeNote.");
        foreach (var tag in c.Tags) { Text(tag, 255, "tag"); Check(!tag.Contains(','), "Tags cannot contain commas."); }
        Check(Directory.Exists(c.ContentDirectory) && (File.GetAttributes(c.ContentDirectory) & FileAttributes.ReparsePoint) == 0, "Content must be a real directory.");
        string[] expected = ["TheTinkererZhHans.dll", "TheTinkererZhHans.json", "TheTinkererZhHans.pck", "README.md"];
        var files = Directory.GetFileSystemEntries(c.ContentDirectory);
        Check(files.Length == 4 && files.Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)), "Content must contain exactly the four translation release files.");
        foreach (var file in files) Check((File.GetAttributes(file) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 && new FileInfo(file).Length > 0, "Content file must be a nonempty regular file.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(c.ContentDirectory, "TheTinkererZhHans.json")));
        Check(manifest.RootElement.GetProperty("id").GetString() == "TheTinkererZhHans" && manifest.RootElement.GetProperty("version").GetString() == "v1.0.0" && !manifest.RootElement.GetProperty("affects_gameplay").GetBoolean(), "Unexpected translation manifest.");
        var assembly = AssemblyName.GetAssemblyName(Path.Combine(c.ContentDirectory, "TheTinkererZhHans.dll"));
        Check(assembly.Name == "TheTinkererZhHans" && assembly.Version == new System.Version(1, 0, 0, 0), "Unexpected DLL identity.");
        Check(Hash(Path.Combine(c.ContentDirectory, "TheTinkererZhHans.dll")) == "433497868549ea8e2d4e73c06580fb7e3f7ce7c23fdee0ebb32fa31c89cc3cda", "DLL differs from tested release.");
        Check(Hash(Path.Combine(c.ContentDirectory, "TheTinkererZhHans.pck")) == "2718e45b8c211c2e4f7c5fea28a5fa034bfae3d7eca6bb53d8dc3f9b6f02234f", "PCK differs from tested release.");
        Check(File.Exists(c.PreviewFile) && new FileInfo(c.PreviewFile).Length is > 0 and < 1048576 && (File.GetAttributes(c.PreviewFile) & FileAttributes.ReparsePoint) == 0, "Preview must be a regular PNG smaller than 1 MiB.");
        Check(File.ReadAllBytes(c.PreviewFile).AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Invalid PNG header.");
        return c;
    }

    static State LoadState(Config c, string configPath)
    {
        Check(SteamUser.BLoggedOn() && SteamApps.BIsSubscribed(), "Steam must be logged on and own Slay the Spire 2.");
        string steamId = SteamUser.GetSteamID().ToString();
        Check(c.ExpectedSteamId == steamId, "Current Steam account differs from expectedSteamId.");
        var s = File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath), Json) ?? throw new InvalidDataException("Empty state.") : new State { ConfigPath = configPath, SteamId = steamId };
        Check(s.AppId == AppId && s.SteamId == steamId && string.Equals(s.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase), "State belongs to another config/app/account.");
        Check(c.ItemId is null || s.ItemId is null || c.ItemId == s.ItemId, "Config conflicts with saved itemId.");
        s.ItemId ??= c.ItemId;
        if (s.ItemId is not null) Check(!c.DependencyIds.Contains(s.ItemId), "Refusing to update a dependency item.");
        return s;
    }

    static void Publish(Config c, string configPath)
    {
        using var stateLock = new FileStream(Path.Combine(ToolDir, "state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var s = LoadState(c, configPath);
        Check(!(s.CreateAttempted && s.ItemId is null), "An item creation has an unknown outcome. Inspect the account and record its verified ID before continuing; automatic duplicate creation is blocked.");
        s.Completed = false; s.LastError = null;
        try
        {
            var existingChildren = Array.Empty<PublishedFileId_t>();
            if (s.ItemId is not null)
            {
                var before = Inspect(new PublishedFileId_t(Id(s.ItemId)), out existingChildren);
                CheckOwned(before, c);
                Check(before.m_rgchTitle == c.Title || (s.CreateResult == "k_EResultOK" && string.IsNullOrWhiteSpace(before.m_rgchTitle)), "Saved item title differs; refusing to change another item.");
            }
            foreach (var dependencyId in c.DependencyIds)
            {
                var d = Inspect(new PublishedFileId_t(Id(dependencyId)), out _);
                Check(d.m_nConsumerAppID.m_AppId == AppId && !d.m_bBanned, "Dependency belongs to another app or is banned.");
            }
            if (s.ItemId is null)
            {
                s.CreateAttempted = true; s.CreateResult = "pending"; Save(s, "creating");
                var created = Wait<CreateItemResult_t>(() => SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity), result =>
                {
                    s.CreateResult = result.m_eResult.ToString(); s.CreateNeedsAgreement = result.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                    if (result.m_nPublishedFileId.m_PublishedFileId != 0) { s.ItemId = result.m_nPublishedFileId.ToString(); s.ItemUrl = Url(s.ItemId); }
                    Save(s, "create-result"); Dump(s);
                });
                Check(created.m_eResult == EResult.k_EResultOK && s.ItemId is not null, "CreateItem failed: " + created.m_eResult);
                Check(s.CreateNeedsAgreement != true, "Workshop agreement requires acceptance: " + s.ItemUrl);
            }
            var item = new PublishedFileId_t(Id(s.ItemId!));
            foreach (var dependencyId in c.DependencyIds)
            {
                var child = new PublishedFileId_t(Id(dependencyId));
                if (!existingChildren.Contains(child))
                {
                    s.DependencyResults[dependencyId] = "pending"; Save(s, "adding-dependency");
                    var added = Wait<AddUGCDependencyResult_t>(() => SteamUGC.AddDependency(item, child), result =>
                    {
                        s.DependencyResults[dependencyId] = result.m_eResult.ToString(); Save(s, "dependency-result"); Dump(s);
                        Check(result.m_nPublishedFileId == item && result.m_nChildPublishedFileId == child, "Dependency callback IDs mismatch.");
                    });
                    Check(added.m_eResult == EResult.k_EResultOK, "AddDependency failed: " + added.m_eResult);
                }
                else s.DependencyResults[dependencyId] = "k_EResultOK";
            }
            // Newly created Steam items remain private until both dependencies are established.
            var update = SteamUGC.StartItemUpdate(new AppId_t(AppId), item);
            Check(update != UGCUpdateHandle_t.Invalid, "StartItemUpdate failed.");
            Check(SteamUGC.SetItemUpdateLanguage(update, "english"), "Default metadata language failed.");
            Check(SteamUGC.SetItemTitle(update, c.Title), "SetItemTitle failed.");
            Check(SteamUGC.SetItemDescription(update, c.Description), "SetItemDescription failed.");
            Check(SteamUGC.SetItemContent(update, c.ContentDirectory), "SetItemContent failed.");
            Check(SteamUGC.SetItemPreview(update, c.PreviewFile), "SetItemPreview failed.");
            Check(SteamUGC.SetItemVisibility(update, Visibility(c.Visibility)), "SetItemVisibility failed.");
            if (c.Tags.Length > 0) Check(SteamUGC.SetItemTags(update, c.Tags), "SetItemTags failed.");
            s.SubmitResult = "pending"; Save(s, "submitting");
            var submitted = Submit(update, c.ChangeNote, result =>
            {
                s.SubmitResult = result.m_eResult.ToString(); s.SubmitNeedsAgreement = result.m_bUserNeedsToAcceptWorkshopLegalAgreement; Save(s, "submit-result"); Dump(s);
                Check(result.m_nPublishedFileId == item, "Upload callback item ID mismatch.");
            });
            Check(submitted.m_eResult == EResult.k_EResultOK && !submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement, "Upload failed or requires Workshop agreement: " + submitted.m_eResult);
            var localized = SteamUGC.StartItemUpdate(new AppId_t(AppId), item);
            Check(localized != UGCUpdateHandle_t.Invalid && SteamUGC.SetItemUpdateLanguage(localized, "schinese"), "Chinese metadata handle failed.");
            Check(SteamUGC.SetItemTitle(localized, c.Title) && SteamUGC.SetItemDescription(localized, c.Description), "Chinese metadata setup failed.");
            s.ChineseResult = "pending"; Save(s, "localizing");
            var chinese = Submit(localized, "添加简体中文标题和说明。", result => { s.ChineseResult = result.m_eResult.ToString(); Save(s, "chinese-result"); Dump(s); });
            Check(chinese.m_eResult == EResult.k_EResultOK && !chinese.m_bUserNeedsToAcceptWorkshopLegalAgreement, "Chinese metadata submission failed: " + chinese.m_eResult);
            var after = Inspect(item, out var children);
            CheckOwned(after, c);
            Check(after.m_rgchTitle == c.Title && after.m_eVisibility == Visibility(c.Visibility), "Remote title/visibility differs.");
            Check(c.DependencyIds.All(id => children.Contains(new PublishedFileId_t(Id(id)))), "Required workshop dependencies missing.");
            s.Completed = true; Save(s, "complete"); Dump(s);
        }
        catch (Exception e) { s.Completed = false; s.LastError = e.Message; Save(s, "failed"); throw; }
    }

    static void Install(Config c, string configPath)
    {
        var s = LoadState(c, configPath);
        Check(s.Completed && s.ItemId is not null, "Only a completed published item can be installed.");
        var item = new PublishedFileId_t(Id(s.ItemId!));
        CheckOwned(Inspect(item, out _), c);
        var subscribed = Wait<RemoteStorageSubscribePublishedFileResult_t>(() => SteamUGC.SubscribeItem(item));
        Check(subscribed.m_eResult == EResult.k_EResultOK, "SubscribeItem failed: " + subscribed.m_eResult);
        bool done = false; EResult result = EResult.k_EResultNone;
        using var callback = Callback<DownloadItemResult_t>.Create(value => { if (value.m_nPublishedFileId == item) { result = value.m_eResult; done = true; } });
        Check(SteamUGC.DownloadItem(item, true), "DownloadItem rejected.");
        var clock = Stopwatch.StartNew();
        while (!done && clock.Elapsed.TotalSeconds < TimeoutSeconds) { SteamAPI.RunCallbacks(); if (!done) Thread.Sleep(25); }
        Check(done && result == EResult.k_EResultOK, "Workshop download failed: " + result);
        Check(SteamUGC.GetItemInstallInfo(item, out ulong size, out string folder, 4096, out uint timestamp), "Install info unavailable.");
        var expected = Directory.GetFiles(c.ContentDirectory);
        Check(Directory.GetFiles(folder).Length == expected.Length, "Downloaded file count differs.");
        foreach (var file in expected) Check(Hash(file) == Hash(Path.Combine(folder, Path.GetFileName(file))), "Downloaded file differs: " + Path.GetFileName(file));
        Dump(new { installed = true, itemId = s.ItemId, folder, size, installedUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp), files = expected.Select(file => new { name = Path.GetFileName(file), sha256 = Hash(file) }) });
    }

    static SteamUGCDetails_t Inspect(PublishedFileId_t item, out PublishedFileId_t[] children)
    {
        var query = SteamUGC.CreateQueryUGCDetailsRequest([item], 1);
        Check(query != UGCQueryHandle_t.Invalid, "Details query handle failed.");
        try
        {
            Check(SteamUGC.SetAllowCachedResponse(query, 0) && SteamUGC.SetReturnChildren(query, true) && SteamUGC.SetReturnLongDescription(query, true), "Details query options failed.");
            var completed = Wait<SteamUGCQueryCompleted_t>(() => SteamUGC.SendQueryUGCRequest(query));
            Check(completed.m_eResult == EResult.k_EResultOK && completed.m_unNumResultsReturned == 1, "Details query failed.");
            Check(SteamUGC.GetQueryUGCResult(query, 0, out var d) && d.m_eResult == EResult.k_EResultOK && d.m_nPublishedFileId == item, "Item details failed.");
            children = new PublishedFileId_t[d.m_unNumChildren];
            if (children.Length > 0) Check(SteamUGC.GetQueryUGCChildren(query, 0, children, (uint)children.Length), "Children query failed.");
            Dump(new { itemId = item.ToString(), owner = d.m_ulSteamIDOwner.ToString(), appId = d.m_nConsumerAppID.m_AppId, creatorAppId = d.m_nCreatorAppID.m_AppId, title = d.m_rgchTitle, description = d.m_rgchDescription, visibility = d.m_eVisibility.ToString(), banned = d.m_bBanned, children = children.Select(x => x.ToString()), fileSize = d.m_nFileSize, totalFilesSize = d.m_ulTotalFilesSize, updatedUtc = DateTimeOffset.FromUnixTimeSeconds(d.m_rtimeUpdated), url = Url(item.ToString()) });
            return d;
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    static void CheckOwned(SteamUGCDetails_t d, Config c) => Check(d.m_ulSteamIDOwner.ToString() == c.ExpectedSteamId && d.m_nConsumerAppID.m_AppId == AppId && d.m_nCreatorAppID.m_AppId == AppId && !d.m_bBanned, "Item owner/app/banned check failed.");
    static SubmitItemUpdateResult_t Submit(UGCUpdateHandle_t handle, string note, Action<SubmitItemUpdateResult_t> callback)
    {
        string? last = null;
        return Wait(() => SteamUGC.SubmitItemUpdate(handle, note), callback, () =>
        {
            var status = SteamUGC.GetItemUpdateProgress(handle, out ulong processed, out ulong total);
            string progress = $"{status}:{processed}:{total}";
            if (progress == last) return; last = progress; Dump(new { uploadStatus = status.ToString(), processed, total });
        });
    }
    static T Wait<T>(Func<SteamAPICall_t> begin, Action<T>? onResult = null, Action? progress = null) where T : struct
    {
        bool done = false; T value = default; Exception? failure = null;
        using var callback = CallResult<T>.Create((result, ioFailure) =>
        {
            try { Check(!ioFailure, typeof(T).Name + ": I/O failure; remote outcome may be unknown."); value = result; onResult?.Invoke(result); }
            catch (Exception e) { failure = e; }
            finally { done = true; }
        });
        var call = begin(); Check(call != SteamAPICall_t.Invalid, typeof(T).Name + ": invalid API call."); callback.Set(call);
        var clock = Stopwatch.StartNew();
        while (!done && clock.Elapsed.TotalSeconds < TimeoutSeconds) { SteamAPI.RunCallbacks(); if (!done) { progress?.Invoke(); Thread.Sleep(25); } }
        Check(done, typeof(T).Name + ": timeout; remote outcome may be unknown; inspect the saved item before retrying.");
        if (failure is not null) throw failure; return value;
    }
    static void Save(State s, string stage)
    {
        s.Stage = stage; s.UpdatedUtc = DateTimeOffset.UtcNow; string temp = StatePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(JsonSerializer.SerializeToUtf8Bytes(s, Json)); stream.Flush(true); }
        if (File.Exists(StatePath)) File.Replace(temp, StatePath, null); else File.Move(temp, StatePath);
    }
    static ERemoteStoragePublishedFileVisibility Visibility(string value) => value switch
    {
        "public" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
        "private" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
        "unlisted" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
        _ => throw new InvalidDataException("Visibility must be public, private, or unlisted.")
    };
    static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    static void Text(string value, int max, string field) => Check(!string.IsNullOrWhiteSpace(value) && !value.Contains('\0') && Encoding.UTF8.GetByteCount(value) <= max, "Invalid " + field);
    static ulong Id(string value) { Check(ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0, "Invalid item/account ID."); return id; }
    static string Url(string id) => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Dump(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    sealed class Config
    {
        public uint AppId { get; set; }
        public string? ItemId { get; set; }
        public string ExpectedSteamId { get; set; } = "";
        public string Title { get; set; } = "";
        public string DescriptionFile { get; set; } = "";
        public string ContentDirectory { get; set; } = "";
        public string PreviewFile { get; set; } = "";
        public string Visibility { get; set; } = "private";
        public string[] DependencyIds { get; set; } = [];
        public string ChangeNote { get; set; } = "";
        public string[] Tags { get; set; } = [];
        [JsonIgnore] public string Description { get; set; } = "";
    }
    sealed class State
    {
        public uint AppId { get; set; } = Program.AppId;
        public string ConfigPath { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string? ItemId { get; set; }
        public string? ItemUrl { get; set; }
        public bool CreateAttempted { get; set; }
        public string? CreateResult { get; set; }
        public bool? CreateNeedsAgreement { get; set; }
        public Dictionary<string, string> DependencyResults { get; set; } = [];
        public string? SubmitResult { get; set; }
        public bool? SubmitNeedsAgreement { get; set; }
        public string? ChineseResult { get; set; }
        public string Stage { get; set; } = "not-started";
        public bool Completed { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
