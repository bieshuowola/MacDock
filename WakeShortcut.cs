namespace MacDock;

internal sealed class WakeShortcut
{
    internal const int MaxKeys = 4;
    readonly int[] keys;
    WakeShortcut(int[] keys) => this.keys = keys;

    internal bool IsSingle => keys.Length == 1;
    internal int[] ToArray() => (int[])keys.Clone();
    internal string Display => string.Join(" + ", keys.Select(Native.KeyName));

    internal static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B;
    internal static int Normalize(int key) => key switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => key
    };

    internal static bool CanRecord(int key) => IsModifier(key) || Native.IsWakeBindingKey(key);

    internal static WakeShortcut? Create(IEnumerable<int> input)
    {
        int[] keys = input.Select(Normalize).Distinct().Take(MaxKeys + 1).ToArray();
        if (keys.Length is 0 or >MaxKeys || keys.Any(key => !CanRecord(key)) ||
            keys.Length == 1 && IsModifier(keys[0])) return null;
        Array.Sort(keys, (a, b) => SortOrder(a).CompareTo(SortOrder(b)));
        return new WakeShortcut(keys);
    }

    static int SortOrder(int key) => key switch
    {
        0x11 => 0,
        0x12 => 1,
        0x10 => 2,
        0x5B => 3,
        _ => key + 4
    };

    internal bool UsesDrawerHotkey => keys is [0x11, 0x12, 0x20];

    internal bool MatchesPress(int rawKey, Func<int, bool> keyHeld)
    {
        int key = Normalize(rawKey);
        if (!keys.Contains(key) || Held(key, keyHeld)) return false;
        foreach (int member in keys)
            if (member != key && !Held(member, keyHeld)) return false;

        // The hook receives keydown before Windows updates asynchronous key state.
        // Sample only on completion so repeats and unrelated typing stay cheap.
        for (int vk = 0x08; vk <= 0xFE; vk++)
            if (!keys.Contains(Normalize(vk)) && keyHeld(vk)) return false;
        return true;
    }

    static bool Held(int key, Func<int, bool> keyHeld) =>
        keyHeld(key) || key == 0x5B && keyHeld(0x5C);
}
