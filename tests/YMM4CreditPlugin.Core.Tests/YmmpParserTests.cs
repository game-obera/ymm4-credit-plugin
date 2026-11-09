using System.Text;
using System.Text.Json;
using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin.Core.Tests;

public class YmmpParserTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ymm4credit-" + Guid.NewGuid().ToString("N"));

    public YmmpParserTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    /// <summary>ymmp を BOM 付き UTF-8 で書き、そのパスを返す</summary>
    private string WriteYmmp(object root)
    {
        var path = Path.Combine(dir, "test.ymmp");
        File.WriteAllText(path, JsonSerializer.Serialize(root), new UTF8Encoding(true));
        return path;
    }

    private static object Item(string type, Dictionary<string, object?> props)
    {
        props["$type"] = $"YukkuriMovieMaker.Project.Items.{type}, YukkuriMovieMaker";
        return props;
    }

    private static object Project(object[] characters, params object[][] timelines) => new Dictionary<string, object?>
    {
        ["FilePath"] = @"C:\proj\self.ymmp",
        ["Timelines"] = timelines.Select(items => new Dictionary<string, object?> { ["Items"] = items }).ToArray(),
        ["Characters"] = characters,
    };

    private static object Character(string name, string api, string arg, string? directory) => new Dictionary<string, object?>
    {
        ["Name"] = name,
        ["Voice"] = new Dictionary<string, object?> { ["API"] = api, ["Arg"] = arg },
        ["TachieCharacterParameter"] = new Dictionary<string, object?> { ["Directory"] = directory },
    };

    [Fact]
    public void ファイル素材を拡張子で分類する()
    {
        var path = WriteYmmp(Project([],
        [
            Item("VideoItem", new() { ["FilePath"] = @"C:\a\rec.mp4" }),
            Item("AudioItem", new() { ["FilePath"] = @"C:\a\bgm.mp3" }),
            Item("ImageItem", new() { ["FilePath"] = @"C:\a\x.PNG" }),
        ]));

        var result = YmmpParser.Parse(path);

        Assert.Contains(new UsedMaterial(MaterialKeyType.File, @"C:\a\rec.mp4", "rec", MaterialKind.Video), result);
        Assert.Contains(new UsedMaterial(MaterialKeyType.File, @"C:\a\bgm.mp3", "bgm", MaterialKind.Other), result);
        Assert.Contains(new UsedMaterial(MaterialKeyType.File, @"C:\a\x.PNG", "x", MaterialKind.Image), result);
    }

    [Fact]
    public void ルートのFilePathは含めない()
    {
        var path = WriteYmmp(Project([], [Item("VideoItem", new() { ["FilePath"] = @"C:\a\rec.mp4" })]));

        var result = YmmpParser.Parse(path);

        Assert.DoesNotContain(result, m => m.Key == @"C:\proj\self.ymmp");
    }

    [Fact]
    public void 立ち絵フォルダ配下の画像は立ち絵1件になる()
    {
        // キャラ設定の立ち絵フォルダ配下なら、どこに置いたフォルダでも立ち絵として扱う(大文字小文字は区別しない)
        var path = WriteYmmp(Project(
            [Character("未使用", "EngineA", "f9", @"C:\chara\キャラA\")],
            [
                Item("TachieItem", new() { ["TachieItemParameter"] = new Dictionary<string, object?> { ["DefaultFace"] = @"C:\chara\キャラA\顔\01.png" } }),
                Item("ImageItem", new() { ["FilePath"] = @"c:\CHARA\キャラA\02.png" }),
            ]));

        var result = YmmpParser.Parse(path);

        var tachie = Assert.Single(result);
        Assert.Equal(new UsedMaterial(MaterialKeyType.Tachie, @"C:\chara\キャラA", "キャラA", MaterialKind.Tachie), tachie);
    }

    [Fact]
    public void 立ち絵フォルダ外の画像はファイル素材()
    {
        // 名前が立ち絵フォルダで始まるだけの別フォルダも対象外
        var path = WriteYmmp(Project(
            [Character("未使用", "EngineA", "f9", @"C:\chara\キャラA")],
            [
                Item("ImageItem", new() { ["FilePath"] = @"C:\chara\キャラA2\01.png" }),
                Item("ImageItem", new() { ["FilePath"] = @"C:\chara\02.png" }),
            ]));

        var result = YmmpParser.Parse(path);

        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Equal(MaterialKeyType.File, m.KeyType));
    }

    [Fact]
    public void 使われているキャラの声と立ち絵()
    {
        var path = WriteYmmp(Project(
            [Character("話者B", "EngineA", "f1", @"C:\chara\キャラA"), Character("未使用", "EngineA", "f9", @"C:\chara\未使用")],
            [Item("VoiceItem", new() { ["CharacterName"] = "話者B" })]));

        var result = YmmpParser.Parse(path);

        Assert.Contains(new UsedMaterial(MaterialKeyType.Voice, "EngineA|f1", "EngineA / f1", MaterialKind.Voice), result);
        Assert.Contains(result, m => m.KeyType == MaterialKeyType.Tachie && m.Key == @"C:\chara\キャラA");
        Assert.DoesNotContain(result, m => m.Key == "EngineA|f9");
        Assert.DoesNotContain(result, m => m.Key == @"C:\chara\未使用");
    }

    [Fact]
    public void 立ち絵アイテムだけのキャラは声を含めない()
    {
        var path = WriteYmmp(Project(
            [Character("話者B", "EngineA", "f1", @"C:\chara\キャラA")],
            [Item("TachieItem", new() { ["CharacterName"] = "話者B" })]));

        var result = YmmpParser.Parse(path);

        Assert.DoesNotContain(result, m => m.KeyType == MaterialKeyType.Voice);
        Assert.Contains(result, m => m.KeyType == MaterialKeyType.Tachie);
    }

    [Fact]
    public void フォントを集め重複を除く()
    {
        var path = WriteYmmp(Project([],
        [
            Item("TextItem", new() { ["Font"] = "フォントA" }),
            Item("VoiceItem", new() { ["Font"] = "フォントA" }),
            Item("TextItem", new() { ["Font"] = "フォントB" }),
        ]));

        var result = YmmpParser.Parse(path);

        Assert.Equal(2, result.Count(m => m.KeyType == MaterialKeyType.Font));
        Assert.Contains(new UsedMaterial(MaterialKeyType.Font, "フォントB", "フォントB", MaterialKind.Font), result);
    }

    [Fact]
    public void 大文字小文字違いのパスは1件()
    {
        var path = WriteYmmp(Project([],
        [
            Item("AudioItem", new() { ["FilePath"] = @"C:\A\b.wav" }),
            Item("AudioItem", new() { ["FilePath"] = @"c:\a\B.wav" }),
        ]));

        var result = YmmpParser.Parse(path);

        Assert.Single(result);
    }

    [Fact]
    public void 複数シーンを辿る()
    {
        var path = WriteYmmp(Project([],
            [Item("AudioItem", new() { ["FilePath"] = @"C:\a\1.wav" })],
            [Item("AudioItem", new() { ["FilePath"] = @"C:\a\2.wav" })]));

        var result = YmmpParser.Parse(path);

        Assert.Contains(result, m => m.Key == @"C:\a\2.wav");
    }

    [Fact]
    public void 壊れたymmpは例外()
    {
        var path = Path.Combine(dir, "broken.ymmp");
        File.WriteAllText(path, "{");

        Assert.Throws<YmmpParseException>(() => YmmpParser.Parse(path));
    }

    [Fact]
    public void 存在しないymmpは例外()
    {
        Assert.Throws<YmmpParseException>(() => YmmpParser.Parse(Path.Combine(dir, "none.ymmp")));
    }
}
