namespace AutomaticLanguageSwitching.NativeHost;

internal interface IKeyboardLayoutService
{
    bool? IsPerAppInputMethodEnabled();
    bool TryEnablePerAppInputMethod();
    string? GetCurrentLayoutId();
    ObservedLayoutSnapshot? GetCurrentLayoutSnapshot();
    string? TryGetStableLayoutIdForStorage(string layoutId);
    LayoutSwitchAttemptResult TrySwitchTo(string layoutId, RestoreAttemptContext context);
}
