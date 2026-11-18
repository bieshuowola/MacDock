using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacDock;

public sealed class Entry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Pinned { get; set; }
    public string Origin {get;set;}="";
    // desktop: mirrored from a real desktop item; system: shell namespace desktop icon;
    // manual: added explicitly by the user and never touched by desktop sync.
    public string Source { get; set; } = "desktop";
    public bool AutoName { get; set; } = true;
    public string? ShellVerb { get; set; }
    [JsonIgnore] public bool IsVirtual => Path.StartsWith("::", StringComparison.Ordinal);
    [JsonIgnore] public bool InDrawer => Source is "desktop" or "system" or "manual";
    [JsonIgnore] public bool IsAlias => Source is "manual" or "legacy" or "retired-auto";
    [JsonIgnore] public string Target => IsVirtual ? "" : Shell.Resolve(Path);
    [JsonIgnore] public string Category => IsVirtual ? "系统" : Classify(Target);
    public static string Classify(string path)
    {
        if (Directory.Exists(path)) return "文件夹";
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".exe" or ".lnk" or ".appref-ms" or ".url" => "应用程序",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".heic" => "图片",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "压缩文件",
            ".mp4" or ".mov" or ".mkv" or ".mp3" or ".wav" or ".flac" => "影音",
            _ => "文档"
        };
    }
}

public sealed class Config
{
    [JsonIgnore] internal bool MigrationPending {get;set;}
    public int Version { get; set; } = 3;
    public string Monitor { get; set; } = "";
    public string Mode { get; set; } = "自定义";
    public bool ReducedMotion { get; set; }
    public bool ListView {get;set;}
    public string Appearance {get;set;}="system";
    public bool AutoCheckUpdates {get;set;}=true;
    public bool DoubleClickToOpen { get; set; }
    public int SuppressWakeKey { get; set; }
    public bool WakeDisabled { get; set; }
    public int DockCapacity { get; set; }
    public int DrawerRows { get; set; }
    public int DrawerColumns { get; set; }
    public List<Entry> Entries { get; set; } = new();
    public List<string> DockOrder {get;set;}=new();
}

public sealed class Store
{
    public string FilePath { get; }
    public Config Data { get; private set; } = new();
    public string? Warning { get; private set; }
    internal bool IsFirstRun { get; private set; }
    string persistedOrder="";
    readonly bool userProfile;
    public Store(string? root = null)
    {
        userProfile=root==null;
        root ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacDock");
        Directory.CreateDirectory(root);
        if(userProfile)ProfileLocation.RequireUserProfile(root);
        FilePath = System.IO.Path.Combine(root, "config.json");
        var history=System.IO.Path.Combine(root,"arrangements");
        IEnumerable<string> Revisions()=>Directory.Exists(history)
            ?Directory.EnumerateFiles(history,"*.json").OrderByDescending(File.GetLastWriteTimeUtc)
            :Array.Empty<string>();
        bool primaryExists=File.Exists(FilePath);
        if (primaryExists)
        {
            try { Data = Read(FilePath); }
            catch(Exception e) when(e is JsonException or InvalidDataException)
            {
                // Never overwrite a corrupt/unsupported original without preserving it.
                File.Copy(FilePath, FilePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+"-"+Guid.NewGuid().ToString("N"));
                Recover(Revisions());
            }
        }
        else if(File.Exists(FilePath+".bak")||File.Exists(FilePath+".tmp")||Revisions().Any()||
            File.Exists(System.IO.Path.Combine(root,"last-session.json"))||Directory.EnumerateFiles(root,"config.json.*").Any())
        {
            Recover(Revisions());
        }
        else IsFirstRun=true;
        persistedOrder=OrderSignature();
    }

    void Recover(IEnumerable<string> revisions)
    {
        var candidates=new[]{FilePath+".tmp",FilePath+".bak"}.Concat(revisions)
            .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc);
        foreach(var candidate in candidates)
        {
            try{Data=Read(candidate);Warning="主配置不可用，已从有效备份恢复。";return;}
            catch(Exception error) when(error is JsonException or InvalidDataException or FileNotFoundException){}
        }
        throw new InvalidDataException("检测到历史配置，但未找到可读备份。现有文件已保留，请从安全备份恢复后再启动。");
    }
    string OrderSignature()=>string.Join("|",PinnedEntries().Select(e=>e.Id))+"/"+string.Join("|",Data.Entries.Select(e=>e.Id));
    static Config Read(string p)
    {
        var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(p)) ?? throw new InvalidDataException();
        if (c.Version is not (1 or 2 or 3) || c.Entries == null || c.Entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Path) || string.IsNullOrWhiteSpace(e.Id) || e.Name == null)) throw new InvalidDataException("Invalid configuration");
        if (c.Entries.Select(e => e.Id).Distinct().Count() != c.Entries.Count) throw new InvalidDataException();
        c.DockOrder??=new();
        if (!new[] { "自定义", "按类型", "首字母" }.Contains(c.Mode)) c.Mode = "自定义";
        if(!new[]{"system","light","dark"}.Contains(c.Appearance))c.Appearance="system";
        if(!Native.IsWakeBindingKey(c.SuppressWakeKey))c.SuppressWakeKey=0;
        if(c.SuppressWakeKey==0)c.WakeDisabled=false;
        c.DockCapacity=Math.Clamp(c.DockCapacity,0,18);c.DrawerRows=Math.Clamp(c.DrawerRows,0,8);c.DrawerColumns=Math.Clamp(c.DrawerColumns,0,12);
        if (c.Version < 3) { if(!File.Exists(p+".before-v3"))File.Copy(p,p+".before-v3");Migrate(c); }
        return c;
    }
    // Version 1 auto-imported the Start Menu. Archive it and retain every record; only real
    // desktop sources participate in the mirrored drawer.
    static void Migrate(Config c)
    {
        // Earlier builds assigned 'manual' to every historical automatic import. That label
        // alone is not evidence of a user action. Keep uncertain records separately, never delete.
        var keep = c.Entries.ToList();
        foreach (var e in keep)
        {
            if(e.Source=="manual"||string.IsNullOrEmpty(e.Source)||e.Source=="desktop"&&!e.IsVirtual&&!Desktop.IsDesktopPath(e.Path))
            { e.Source="legacy";e.Origin="旧版记录，主动添加来源未确认"; }
        }
        c.Entries = keep;
        c.Version = 3;
        c.MigrationPending=true;
    }
    public bool Add(string path, bool pin, string? name = null, string source = "manual")
    {
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        var old = Data.Entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (old != null) { old.Pinned |= pin;if(old.Source is "legacy" or "retired-auto"){old.Source="manual";old.Origin="user-added";}return false; }
        Data.Entries.Add(new Entry { Path = path, Pinned = pin, Source = source,Origin=source=="manual"?"user-added":"desktop", Name = name ?? (Directory.Exists(path) ? new DirectoryInfo(path).Name : System.IO.Path.GetFileNameWithoutExtension(path)) });
        return true;
    }
    public void Save()=>SaveCore(false);

    internal void SaveSettings()
    {
        try{SaveCore(true);}
        finally{try{File.Delete(FilePath+".settings-tmp");}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    void SaveCore(bool settings)
    {
        if(userProfile)ProfileLocation.RequireUserProfile(System.IO.Path.GetDirectoryName(FilePath)!);
        Data.DockOrder=PinnedEntries().Select(e=>e.Id).ToList();
        var order=OrderSignature();
        var tmp = FilePath + (settings?".settings-tmp":".tmp");
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(f, Data, new JsonSerializerOptions { WriteIndented = true }); f.Flush(true);
        }
        if (File.Exists(FilePath))
        {
            try { Read(FilePath); File.Copy(FilePath, FilePath + ".bak", true); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { }
        }
        var expected=File.ReadAllBytes(tmp);
        // ReplaceFile merges EFS metadata and can fail on an encrypted profile.
        // Same-directory MoveFileEx is an atomic rename; the flushed temp inherits this directory's ACL/EFS policy.
        if (!Native.MoveFileEx(tmp, FilePath, 0x9))
        {
            var error=System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if(settings||error!=17 && error!=5) throw new System.ComponentModel.Win32Exception(error);
            // Some encrypted/virtualized profiles reject rename across their internal backing volumes.
            // Keep both the previous backup and complete .tmp until a durable write finishes.
            using(var source=File.OpenRead(tmp))
            using(var dest=new FileStream(FilePath,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) { source.CopyTo(dest);dest.Flush(true); }
            File.Delete(tmp);
        }
        if(!File.ReadAllBytes(FilePath).AsSpan().SequenceEqual(expected))
            throw new IOException("配置写入校验失败，当前排列尚未确认保存。原有备份已保留。");
        if(userProfile)ProfileLocation.RequireUserProfile(System.IO.Path.GetDirectoryName(FilePath)!);
        // Only a verified committed write becomes a layout revision. A failed
        // rename must not leave a journal record that looks like a saved layout.
        if(!settings&&order!=persistedOrder)
        {
            try
            {
                var history=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FilePath)!,"arrangements");
                Directory.CreateDirectory(history);
                File.WriteAllBytes(System.IO.Path.Combine(history,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+"-"+Guid.NewGuid().ToString("N")+".json"),expected);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException)
            { Warning="配置已保存，但历史布局副本未能写入："+e.Message; }
        }
        if(!settings)persistedOrder=order;
        IsFirstRun=false;
    }
    public void Reorder(string source, string target)
    {
        var a = Data.Entries.FindIndex(e => e.Id == source); var b = Data.Entries.FindIndex(e => e.Id == target);
        if (a < 0 || b < 0 || a == b) return;
        var item = Data.Entries[a]; Data.Entries.RemoveAt(a); Data.Entries.Insert(b, item);
    }
    public IReadOnlyList<Entry> PinnedEntries()
    {
        var pins=Data.Entries.Where(e=>e.Pinned).ToDictionary(e=>e.Id);
        return (Data.DockOrder??new()).Distinct().Where(pins.ContainsKey).Select(id=>pins[id]).Concat(pins.Values.Where(e=>!(Data.DockOrder??new()).Contains(e.Id))).ToArray();
    }
    public void SortScope(IReadOnlyList<string> scope,string source,int index,bool dock)
    {
        Data.DockOrder=PinnedEntries().Select(e=>e.Id).ToList();
        var order=scope.ToList();if(!order.Remove(source))return;order.Insert(Math.Clamp(index,0,order.Count),source);
        if(dock){var all=PinnedEntries().Select(e=>e.Id).ToList();int n=0;for(int i=0;i<all.Count;i++)if(scope.Contains(all[i]))all[i]=order[n++];Data.DockOrder=all;}
        else{var byId=Data.Entries.ToDictionary(e=>e.Id);int n=0;for(int i=0;i<Data.Entries.Count;i++)if(scope.Contains(Data.Entries[i].Id))Data.Entries[i]=byId[order[n++]];}
    }
    // Reconcile only mirrored (desktop/system) entries. Manual entries and user ordering are untouched.
    public bool Sync(IReadOnlyList<DesktopItem> items,IReadOnlySet<string>? readableFolders=null)
    {
        var changed = false;
        for (var i = Data.Entries.Count - 1; i >= 0; i--)
        {
            var e = Data.Entries[i];
            if (e.Source is not ("desktop" or "system")) continue;
            if(e.Source=="desktop" && readableFolders!=null && !readableFolders.Contains(System.IO.Path.GetDirectoryName(e.Path)??""))continue;
            if (items.Any(x => string.Equals(x.Path, e.Path, StringComparison.OrdinalIgnoreCase))) continue;
            if(e.Source=="system") {e.Source=e.Pinned?"legacy":"retired-auto";e.Origin="旧版自动导入，未在真实桌面视图中出现";}
            else if(e.Pinned){e.Source="manual";e.Origin="pinned-desktop-unavailable";}
            else Data.Entries.RemoveAt(i);
            changed = true;
        }
        foreach (var item in items)
        {
            var e = Data.Entries.FirstOrDefault(x => string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            if (e == null)
            {
                Data.Entries.Add(new Entry { Path = item.Path, Name = item.Name, Source = item.Source, ShellVerb = item.Shell, Pinned = false });
                changed = true;
                continue;
            }
            if (e.Source != item.Source) { e.Source = item.Source; changed = true; }
            if (e.ShellVerb != item.Shell) { e.ShellVerb = item.Shell; changed = true; }
            if (e.AutoName && e.Name != item.Name) { e.Name = item.Name; changed = true; }
        }
        return changed;
    }
    internal void Initialize(IReadOnlyList<DesktopItem> items,IReadOnlyList<DesktopView.PositionedItem> positions,IEnumerable<DesktopItem> defaults,
        int columns=1,int rows=1)
    {
        if(!IsFirstRun||Data.Entries.Count!=0)return;
        var byPath=new Dictionary<string,DesktopItem>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in items)byPath.TryAdd(item.Path,item);
        var ordered=new List<DesktopItem>();
        foreach(var position in positions)
            if(byPath.Remove(position.Path,out var item))ordered.Add(item);
        // When Explorer is not ready, keep all mirrored items in the scan's stable order.
        foreach(var item in items)
            if(byPath.Remove(item.Path))ordered.Add(item);
        // The existing WrapPanel renders row by row. Seed each visible group of rows
        // in column reading order once, without changing the later drag/scroll rules.
        foreach(var page in ordered.Chunk(columns*rows))
        {
            var visual=new DesktopItem[page.Length];
            int next=0;
            foreach(int slot in Enumerable.Range(0,page.Length).OrderBy(slot=>slot%columns))
                visual[slot]=page[next++];
            foreach(var item in visual)SyncInitialEntry(item);
        }
        foreach(var item in defaults)
        {
            var existing=Data.Entries.FirstOrDefault(e=>e.Path.Equals(item.Path,StringComparison.OrdinalIgnoreCase));
            if(existing==null)existing=SyncInitialEntry(item);
            existing.Pinned=true;
            Data.DockOrder.Add(existing.Id);
        }
        Save();
    }

    Entry SyncInitialEntry(DesktopItem item)
    {
        var entry=new Entry{Path=item.Path,Name=item.Name,Source=item.Source,ShellVerb=item.Shell};
        Data.Entries.Add(entry);
        return entry;
    }
    public bool Repoint(string oldPath, string newPath,bool maintainDesktopBoundary=false)
    {
        var e = Data.Entries.FirstOrDefault(x => string.Equals(x.Path, oldPath, StringComparison.OrdinalIgnoreCase));
        if (e == null) return false;
        if(maintainDesktopBoundary&&e.Source=="desktop"&&!Desktop.IsDesktopPath(newPath)){
            if(!e.Pinned){Data.Entries.Remove(e);return true;}
            e.Source="manual";e.Origin="user-added"; // an explicit pin follows the object as an alias
        }
        e.Path = newPath;
        if (e.AutoName) e.Name = Desktop.DisplayName(newPath);
        return true;
    }
}

public static class Alphabet
{
    static readonly int[] Limits = { -20319,-20284,-19776,-19219,-18711,-18527,-18240,-17923,-17418,-16475,-16213,-15641,-15166,-14923,-14915,-14631,-14150,-14091,-13319,-12839,-12557,-11848,-11056,-10247 };
    const string Letters = "ABCDEFGHJKLMNOPQRSTWXYZ";
    static Alphabet() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static string Initial(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "#";
        var c = s.Trim()[0];
        if (char.IsAsciiLetter(c)) return char.ToUpperInvariant(c).ToString();
        if (c < 0x4e00 || c > 0x9fff) return "#";
        var b = Encoding.GetEncoding(936, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetBytes(c.ToString());
        if (b.Length != 2) return "#";
        int code = b[0] * 256 + b[1] - 65536;
        if (code > -10247) return "#";
        for (var i = Limits.Length - 2; i >= 0; i--) if (code >= Limits[i]) return Letters[i].ToString();
        return "#";
    }
    public static IEnumerable<IGrouping<string, Entry>> Groups(IEnumerable<Entry> entries, string mode)
    {
        if (mode == "自定义") return entries.GroupBy(_ => "");
        var compare = StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true);
        return entries.OrderBy(e => e.Name, compare).GroupBy(e => mode == "按类型" ? e.Category : Initial(e.Name)).OrderBy(g => g.Key, compare);
    }
}

// Deterministic intent state machine. It never consumes or modifies input.
public sealed class Intent
{
    public long Since { get; private set; } = -1;
    public int Delay { get; private set; }
    public void Reset() => Since = -1;
    public bool Feed(long now, bool inside, bool pressed, bool explicitDrag, bool approaching)
    {
        if (!inside || (pressed && !explicitDrag)) { Reset(); return false; }
        if (Since < 0) { Since = now; Delay = explicitDrag ? 650 : approaching ? 100 : 180; }
        return now - Since >= Delay;
    }
}
