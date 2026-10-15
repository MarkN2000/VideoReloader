using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using BepisModSettings.ConfigAttributes;
using BepisResoniteWrapper;
using Elements.Core;
using FrooxEngine;
using NYoutubeDL.Options;

namespace VideoReloader;

[ResonitePlugin("markn2000.VideoReloader", "VideoReloader", "0.1.0", "markn2000", "https://github.com/MarkN2000/VideoReloader")]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID)]
[BepInDependency(BepisResoniteWrapper.PluginMetadata.GUID)]
[BepInDependency("ResoniteModding.BepisModSettings", "1.6.6")]
public sealed class Plugin : BasePlugin
{
    private ConfigEntry<bool> _automaticReload = null!;
    private Action<VideoTextureProvider> _freeAsset = null!;
    private WorldManager? _worldManager;
    private (CookiesBrowser, VideoStreamingServicesSettings.PreferredVideoResolution)? _lastSettings;
    private volatile bool _ready;
    private long _requestVersion;
    private long _automaticVersion;

    public override void Load()
    {
        // 共有フィールドを操作せず、本体のローカルアセット解放だけを呼ぶ。
        _freeAsset = CreateFreeAssetDelegate();
        Config.SaveOnConfigSet = false;
        _automaticReload = Config.Bind("Reloading", "Auto-Reload on Settings Change", true,
            "Automatically reload videos locally 1 second after changing the cookie browser or Preferred Resolution.");
        Config.Bind("Reloading", "Reload Session Videos (Local)", default(dummy),
            new ConfigDescription("Reload all videos in the current session locally. Video and audio may briefly stop.",
                null, new ActionConfig((Action)(() => RequestReload(false)))));
        _automaticReload.SettingChanged += AutomaticReloadChanged;
        if (!File.Exists(Config.ConfigFilePath)) Config.Save();

        if (Engine.Current?.IsReady == true) OnEngineReady();
        else ResoniteHooks.OnEngineReady += OnEngineReady;
    }

    private static Action<VideoTextureProvider> CreateFreeAssetDelegate() =>
        (typeof(VideoTextureProvider).GetMethod("FreeAsset", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingMethodException(typeof(VideoTextureProvider).FullName, "FreeAsset"))
        .CreateDelegate<Action<VideoTextureProvider>>();

    private void OnEngineReady()
    {
        ResoniteHooks.OnEngineReady -= OnEngineReady;
        if (_ready) return;
        try
        {
            _worldManager = Engine.Current.WorldManager;
            _worldManager.WorldFocused += InvalidateRequests;
            _lastSettings = null;
            _ready = true;
            Settings.RegisterValueChanges<VideoStreamingServicesSettings>(SettingsChanged);
            Log.LogInfo("Local video reloading enabled.");
        }
        catch (Exception error)
        {
            Unload();
            Log.LogError($"Initialization failed; video reloading disabled: {error}");
        }
    }

    private void SettingsChanged(VideoStreamingServicesSettings settings)
    {
        if (settings == null)
        {
            _lastSettings = null;
            Interlocked.Increment(ref _automaticVersion);
            return;
        }
        if (RememberSettings(settings.UseCookiesFromBrowser.Value, settings.PreferredResolution.Value)
            && _automaticReload.Value) RequestReload(true);
    }

    private bool RememberSettings(CookiesBrowser cookies, VideoStreamingServicesSettings.PreferredVideoResolution resolution)
    {
        var current = (cookies, resolution);
        bool changed = _lastSettings.HasValue && _lastSettings.Value != current;
        _lastSettings = current;
        return changed;
    }

    private void AutomaticReloadChanged(object? sender, EventArgs args) => Interlocked.Increment(ref _automaticVersion);
    private void InvalidateRequests(World _) => Interlocked.Increment(ref _requestVersion);

    private bool IsCurrentRequest(long request, long automaticVersion, bool automatic) =>
        _ready && request == Volatile.Read(ref _requestVersion)
        && (!automatic || automaticVersion == Volatile.Read(ref _automaticVersion));

    private bool CanRun(World world, long request, long automaticVersion, bool automatic) =>
        IsCurrentRequest(request, automaticVersion, automatic)
        && (!automatic || _automaticReload.Value)
        && !world.IsDisposed && !world.IsDestroyed && _worldManager?.FocusedWorld == world;

    private void RequestReload(bool automatic)
    {
        if (!_ready) return;
        var world = _worldManager?.FocusedWorld;
        if (world == null || world.IsDisposed || world.IsDestroyed || world.IsUserspace()) return;
        long request = Interlocked.Increment(ref _requestVersion);
        long automaticVersion = Volatile.Read(ref _automaticVersion);
        world.RunSynchronously(() =>
        {
            if (!CanRun(world, request, automaticVersion, automatic)) return;
            // ponytail: stale callbacks remain queued until due; use cancellable scheduling if frequent changes make queue growth measurable.
            if (automatic)
                world.RunInSeconds(1f, () => Reload(world, request, automaticVersion, automatic));
            else
                Reload(world, request, automaticVersion, automatic);
        });
    }

    private void Reload(World world, long request, long automaticVersion, bool automatic)
    {
        if (!CanRun(world, request, automaticVersion, automatic)) return;
        try
        {
            var videos = world.RootSlot.GetComponentsInChildren<VideoTextureProvider>(excludeDisabled: false, includeLocal: true);
            Log.LogInfo($"Found {videos.Count} video providers to reload.");
            foreach (var video in videos)
            {
                // 所有Componentの更新スコープは本体に設定させる。実行直前にも要求と対象の存続を確認する。
                video.RunSynchronously(() =>
                {
                    if (!CanRun(world, request, automaticVersion, automatic) || video.IsRemoved || !video.IsStarted) return;
                    try
                    {
                        _freeAsset(video);
                        video.MarkChangeDirty();
                    }
                    catch (Exception error)
                    {
                        Log.LogError($"Failed to request a reload for a video provider: {error}");
                    }
                });
            }
        }
        catch (Exception error)
        {
            Log.LogError($"Failed to enumerate videos or schedule reloads: {error}");
        }
    }

    public override bool Unload()
    {
        bool wasReady = _ready;
        _ready = false;
        Interlocked.Increment(ref _requestVersion);
        ResoniteHooks.OnEngineReady -= OnEngineReady;
        if (_automaticReload != null) _automaticReload.SettingChanged -= AutomaticReloadChanged;
        if (_worldManager != null) _worldManager.WorldFocused -= InvalidateRequests;
        if (wasReady) Settings.UnregisterValueChanges<VideoStreamingServicesSettings>(SettingsChanged);
        return true;
    }
}
