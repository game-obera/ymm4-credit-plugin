using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin.Core.Tests;

public class CreditDatabaseTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ymm4credit-" + Guid.NewGuid().ToString("N"));

    public CreditDatabaseTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Addは重複しない()
    {
        var db = new CreditDatabase();

        db.Add(new UsedMaterial(MaterialKeyType.File, @"C:\A\b.wav", "b", MaterialKind.Other));
        db.Add(new UsedMaterial(MaterialKeyType.File, @"c:\a\B.wav", "B", MaterialKind.Other));

        var entry = Assert.Single(db.Materials);
        Assert.Equal("b", entry.Name);
        Assert.Equal(MaterialKind.Other, entry.Kind);
        Assert.Equal("", entry.Source);
        Assert.Equal("", entry.Credit);
    }

    [Fact]
    public void Containsは大文字小文字を無視する()
    {
        var db = new CreditDatabase();
        db.Add(new UsedMaterial(MaterialKeyType.File, @"C:\A\b.wav", "b", MaterialKind.Other));

        Assert.True(db.Contains(new UsedMaterial(MaterialKeyType.File, @"c:\a\B.WAV", "B", MaterialKind.Other)));
        Assert.False(db.Contains(new UsedMaterial(MaterialKeyType.Font, @"C:\A\b.wav", "b", MaterialKind.Font)));
    }

    [Fact]
    public void SyncSourcesは新しい出典だけ足す()
    {
        var db = new CreditDatabase();
        db.Sources.Add(new SourceSetting { Name = "出典1", Merge = true, MergedCredit = "X" });
        db.Materials.Add(new MaterialEntry { Key = "a", Source = "出典1" });
        db.Materials.Add(new MaterialEntry { Key = "b", Source = "出典2" });
        db.Materials.Add(new MaterialEntry { Key = "c", Source = "" });

        db.SyncSources();

        Assert.Equal(2, db.Sources.Count);
        var source1 = db.FindSource("出典1")!;
        Assert.True(source1.Merge);
        Assert.Equal("X", source1.MergedCredit);
        var source2 = db.FindSource("出典2")!;
        Assert.False(source2.Merge);
        Assert.Equal("", source2.MergedCredit);
    }

    [Fact]
    public void SyncSourcesはどの素材にも使われていない出典を消す()
    {
        var db = new CreditDatabase();
        db.Sources.Add(new SourceSetting { Name = "出典1", Merge = true, MergedCredit = "X" });
        db.Sources.Add(new SourceSetting { Name = "出典2", Merge = true });
        db.Sources.Add(new SourceSetting { Name = "削除した素材の出典" });
        db.Materials.Add(new MaterialEntry { Key = "a", Source = "出典1" });

        db.SyncSources();

        var source1 = Assert.Single(db.Sources);
        Assert.Equal(("出典1", true, "X"), (source1.Name, source1.Merge, source1.MergedCredit));
    }

    [Fact]
    public void SyncSourcesは出典の前後の空白を取り除く()
    {
        var db = new CreditDatabase();
        db.Materials.Add(new MaterialEntry { Key = "a", Source = "出典" });
        db.Materials.Add(new MaterialEntry { Key = "b", Source = " 出典　" });

        db.SyncSources();

        Assert.Single(db.Sources);
        Assert.All(db.Materials, m => Assert.Equal("出典", m.Source));
    }

    [Fact]
    public void 保存して読み戻せる()
    {
        var path = Path.Combine(dir, "credit-data.json");
        var db = new CreditDatabase();
        db.Materials.Add(new MaterialEntry
        {
            KeyType = MaterialKeyType.Voice,
            Key = "EngineA|f1",
            Name = "話者A",
            Source = "EngineA",
            Credit = "EngineA",
            Kind = MaterialKind.Voice,
        });
        db.Sources.Add(new SourceSetting { Name = "EngineA", Merge = true, MergedCredit = "音声:EngineA", MergedKind = MaterialKind.Voice });
        db.Sources.Add(new SourceSetting { Name = "出典" });

        CreditDatabaseStore.Save(path, db);
        var loaded = CreditDatabaseStore.Load(path);

        var e = Assert.Single(loaded.Materials);
        Assert.Equal((MaterialKeyType.Voice, "EngineA|f1", "話者A", "EngineA", "EngineA", MaterialKind.Voice),
            (e.KeyType, e.Key, e.Name, e.Source, e.Credit, e.Kind));
        Assert.Equal(2, loaded.Sources.Count);
        var s = loaded.Sources[0];
        Assert.Equal(("EngineA", true, "音声:EngineA", (MaterialKind?)MaterialKind.Voice), (s.Name, s.Merge, s.MergedCredit, s.MergedKind));
        Assert.Null(loaded.Sources[1].MergedKind);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ファイルが無ければ空()
    {
        var db = CreditDatabaseStore.Load(Path.Combine(dir, "none.json"));

        Assert.Empty(db.Materials);
        Assert.Empty(db.Sources);
    }

    [Fact]
    public void 壊れたJSONは例外で元ファイルはそのまま()
    {
        var path = Path.Combine(dir, "credit-data.json");
        File.WriteAllText(path, "{x");

        Assert.Throws<CreditDatabaseLoadException>(() => CreditDatabaseStore.Load(path));
        Assert.Equal("{x", File.ReadAllText(path));
    }
}
