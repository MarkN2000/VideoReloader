using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

internal static class Program
{
    private static int _checks;

    private static int Main(string[] args)
    {
        string game = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\Resonite";
        string gale = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.kesomannen.gale");
        string profile = args.Length > 1 ? args[1] : Path.Combine(gale, @"resonite\profiles\Mod開発");
        string settings = args.Length > 2 ? args[2] : Path.Combine(gale,
            @"cache\ResoniteModding-BepisModSettings\1.6.6\BepInEx\plugins\ResoniteModding-BepisModSettings\BepisModSettings");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string? file = new[] { game, Path.Combine(profile, "BepInEx", "core"), settings }
                .Select(dir => Path.Combine(dir, name.Name + ".dll")).FirstOrDefault(File.Exists)
                ?? Directory.EnumerateFiles(Path.Combine(profile, "BepInEx", "plugins"), name.Name + ".dll", SearchOption.AllDirectories).FirstOrDefault();
            return file == null ? null : context.LoadFromAssemblyPath(file);
        };
        try
        {
            Run();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var assembly = Assembly.Load("VideoReloader");
        var type = assembly.GetType("VideoReloader.Plugin", throwOnError: true)!;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        // コンストラクタ・Load・Engine初期化を実行せず、判断処理だけを検査する。
        var plugin = RuntimeHelpers.GetUninitializedObject(type);
        object? Call(string method, params object?[] values) => type.GetMethod(method, members)!.Invoke(plugin, values);
        void Set(string field, object value) => type.GetField(field, members)!.SetValue(plugin, value);

        var free = (Delegate)Call("CreateFreeAssetDelegate")!;
        Check(free.Method.Name == "FreeAsset" && free.Method.DeclaringType?.FullName == "FrooxEngine.VideoTextureProvider",
            "既存のFreeAssetを指すデリゲートを取得できる");
        Check(free.Target == null, "特定の動画へ固定されていないデリゲート");

        var parameters = type.GetMethod("RememberSettings", members)!.GetParameters();
        var browsers = Enum.GetValues(parameters[0].ParameterType);
        var resolutions = Enum.GetValues(parameters[1].ParameterType);
        bool Observe(int browser, int resolution) => (bool)Call("RememberSettings", browsers.GetValue(browser), resolutions.GetValue(resolution))!;
        Check(!Observe(0, 0), "初回通知では実行しない");
        Check(!Observe(0, 0), "同じ値の通知では実行しない");
        Check(Observe(1, 0), "使用ブラウザの変更を検知する");
        Check(!Observe(1, 0), "変更後の重複通知を除外する");
        Check(Observe(1, 1), "解像度の変更を検知する");
        Check(Observe(0, 0), "両方を元へ戻す変更も検知する");
        Call("SettingsChanged", new object?[] { null });
        Check(!Observe(1, 1), "設定が一度なくなった後の初回通知でも実行しない");

        bool Current(long request, long automaticVersion, bool automatic) => (bool)Call("IsCurrentRequest", request, automaticVersion, automatic)!;
        Check(!Current(0, 0, false), "準備前の手動要求を実行しない");
        Set("_ready", true);
        Set("_automaticVersion", 0L);
        Check(Current(0, 0, true), "最新の自動要求を受け付ける");
        Set("_requestVersion", 2L);
        Check(!Current(0, 0, true) && !Current(1, 0, true) && Current(2, 0, true), "連続変更では最新の予約だけが有効");
        Call("InvalidateRequests", new object?[] { null });
        Check(!Current(2, 0, false) && Current(3, 0, false), "セッション切り替えで古い要求を無効にする");
        Call("InvalidateRequests", new object?[] { null });
        Check(!Current(2, 0, true), "セッションを離れて戻っても古い要求は復活しない");
        Call("AutomaticReloadChanged", null, EventArgs.Empty);
        Check(!Current(4, 0, true), "自動設定切り替えで待機中の自動要求を無効にする");
        Check(Current(4, 0, false), "自動設定切り替えで手動要求を無効にしない");
        Call("AutomaticReloadChanged", null, EventArgs.Empty);
        Check(!Current(4, 0, true) && Current(4, 2, true), "再びONにしても古い自動要求は復活しない");
        Set("_ready", false);
        Check(!Current(4, 2, true) && !Current(4, 2, false), "無効化後はどちらの要求も実行しない");

        using var module = ModuleDefinition.ReadModule(assembly.Location);
        var allowed = new HashSet<string>
        {
            "FrooxEngine.Settings::RegisterValueChanges", "FrooxEngine.Settings::UnregisterValueChanges",
            "FrooxEngine.SettingUpdateHandler`1<FrooxEngine.VideoStreamingServicesSettings>::.ctor",
            "FrooxEngine.WorldManager::add_WorldFocused", "FrooxEngine.WorldManager::remove_WorldFocused",
            "FrooxEngine.World::RunSynchronously", "FrooxEngine.World::RunInSeconds",
            "FrooxEngine.WorldExtensions::IsUserspace", "FrooxEngine.Slot::GetComponentsInChildren",
            "FrooxEngine.ComponentBase`1<FrooxEngine.Component>::RunSynchronously",
            "FrooxEngine.ComponentBase`1<FrooxEngine.Component>::MarkChangeDirty"
        };
        int engineCalls = 0;
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is MethodReference call)
            {
                string owner = call.DeclaringType.FullName;
                Check(!owner.StartsWith("HarmonyLib.", StringComparison.Ordinal), "ModからHarmonyを呼ばない");
                if (owner.StartsWith("FrooxEngine.", StringComparison.Ordinal))
                {
                    engineCalls++;
                    Check(call.Name.StartsWith("get_", StringComparison.Ordinal) || allowed.Contains(owner + "::" + call.Name),
                        "本体への呼び出しを読取・通知購読・更新予約に限定: " + call.FullName);
                }
                if (owner.StartsWith("System.Reflection.", StringComparison.Ordinal) || call.Name == "GetMethod")
                    Check(method.Name == "CreateFreeAssetDelegate", "リフレクションを解放処理の取得だけに限定");
            }
            if (instruction.OpCode.Code is Mono.Cecil.Cil.Code.Stfld or Mono.Cecil.Cil.Code.Stsfld)
            {
                var field = (FieldReference)instruction.Operand;
                Check(!field.DeclaringType.FullName.StartsWith("FrooxEngine.", StringComparison.Ordinal), "本体フィールドへ直接書き込まない");
            }
        }
        Check(engineCalls > 0, "検査対象の本体呼び出しが存在する");
        Console.WriteLine($"PASS: {_checks}検査。初回・同値通知、予約の無効化、デリゲート取得、共有書込・パッチの不在。");
        Console.WriteLine("ゲーム起動・動画読み込み・DLL配置は実行していません。実機動作とワールド側の間接反応は未確認です。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
