using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin.Core.Tests;

public class CreditBuilderTests
{
    private readonly CreditDatabase db = new();
    private readonly List<UsedMaterial> used = [];

    /// <summary>使用素材として加え、同時に登録もする</summary>
    private void Register(string key, MaterialKind kind, string credit, string source = "", MaterialKeyType type = MaterialKeyType.File)
    {
        used.Add(new UsedMaterial(type, key, key, kind));
        db.Materials.Add(new MaterialEntry { KeyType = type, Key = key, Name = key, Kind = kind, Credit = credit, Source = source });
    }

    [Fact]
    public void 種類ごとに見出しを付けて並べる()
    {
        Register(@"C:\a\bgm.mp3", MaterialKind.Bgm, "出典名 https://example.com/");
        Register(@"C:\chara\A", MaterialKind.Tachie, "立ち絵:A", type: MaterialKeyType.Tachie);

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■立ち絵\r\n立ち絵:A\r\n■BGM\r\n出典名 https://example.com/\r\n", text);
    }

    [Fact]
    public void 複数行のクレジットは改行をCRLFにそろえ末尾の空行を除く()
    {
        Register(@"C:\a\bgm.mp3", MaterialKind.Bgm, "曲名\n作曲：A\r\nhttps://a.example/\r\n\r\n");

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■BGM\r\n曲名\r\n作曲：A\r\nhttps://a.example/\r\n", text);
    }

    [Fact]
    public void 掲載不要の素材はクレジットにも該当なしにも出さない()
    {
        Register(@"C:\a\bgm.mp3", MaterialKind.Bgm, "BGM");
        Register(@"C:\a\rec.mp4", MaterialKind.Video, "");
        db.Materials[^1].NoCredit = true;

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■BGM\r\nBGM\r\n", text);
    }

    [Fact]
    public void 掲載不要の出典の素材は出さない()
    {
        Register(@"C:\a\se1.wav", MaterialKind.SoundEffect, "a", "自作");
        Register(@"C:\a\se2.wav", MaterialKind.SoundEffect, "b", "出典");
        db.Sources.Add(new SourceSetting { Name = "自作", NoCredit = true });

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■効果音\r\nb\r\n", text);
    }

    [Fact]
    public void まとめる出典は1行()
    {
        Register(@"C:\a\se1.wav", MaterialKind.SoundEffect, "a", "出典");
        Register(@"C:\a\se2.wav", MaterialKind.SoundEffect, "b", "出典");
        Register(@"C:\a\se3.wav", MaterialKind.SoundEffect, "c", "出典");
        db.Sources.Add(new SourceSetting { Name = "出典", Merge = true, MergedCredit = "効果音：出典" });

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■効果音\r\n効果音：出典\r\n", text);
    }

    [Fact]
    public void まとめない出典は素材ごと()
    {
        Register(@"C:\a\bgm1.mp3", MaterialKind.Bgm, "BGM1 / 出典", "出典");
        Register(@"C:\a\bgm2.mp3", MaterialKind.Bgm, "BGM2 / 出典", "出典");
        db.Sources.Add(new SourceSetting { Name = "出典", Merge = false, MergedCredit = "使われない" });

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■BGM\r\nBGM1 / 出典\r\nBGM2 / 出典\r\n", text);
    }

    [Fact]
    public void まとめる出典が種類をまたぐときは先頭の種類に1行()
    {
        Register(@"C:\a\se1.wav", MaterialKind.SoundEffect, "", "出典1");
        Register(@"C:\a\bgm1.mp3", MaterialKind.Bgm, "", "出典1");
        Register(@"C:\a\se2.wav", MaterialKind.SoundEffect, "効果音：別", "出典2");
        db.Sources.Add(new SourceSetting { Name = "出典1", Merge = true, MergedCredit = "音楽：出典1" });

        var text = CreditBuilder.Build(used, db);

        // 出力順で先頭の BGM にだけ出す。効果音の見出しはほかの出典の行で残る
        Assert.Equal("■BGM\r\n音楽：出典1\r\n■効果音\r\n効果音：別\r\n", text);
    }

    [Fact]
    public void まとめる出典は出力先の種類を指定できる()
    {
        Register(@"C:\chara\キャラ", MaterialKind.Tachie, "", "キャラ", MaterialKeyType.Tachie);
        Register(@"C:\chara\キャラ\顔\1.png", MaterialKind.Image, "", "キャラ");
        db.Sources.Add(new SourceSetting { Name = "キャラ", Merge = true, MergedCredit = "立ち絵：キャラ", MergedKind = MaterialKind.Image });

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■画像\r\n立ち絵：キャラ\r\n", text);
    }

    [Fact]
    public void 同じ文面は見出し内で1回()
    {
        Register(@"C:\a\img1.png", MaterialKind.Image, "画像：出典");
        Register(@"C:\a\img2.png", MaterialKind.Image, "画像：出典");

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■画像\r\n画像：出典\r\n", text);
    }

    [Fact]
    public void 未登録とクレジット空は該当なし()
    {
        Register(@"C:\a\bgm.mp3", MaterialKind.Bgm, "BGM");
        used.Add(new UsedMaterial(MaterialKeyType.File, @"C:\a\rec.mp4", "a", MaterialKind.Video));
        Register("フォントB", MaterialKind.Font, "", type: MaterialKeyType.Font);

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■BGM\r\nBGM\r\n\r\n# --- クレジット該当なし ---\r\n# [動画] C:\\a\\rec.mp4\r\n# [フォント] フォントB\r\n", text);
    }

    [Fact]
    public void 該当なしだけのときは空行を入れない()
    {
        used.Add(new UsedMaterial(MaterialKeyType.Voice, "EngineA|f1", "EngineA / f1", MaterialKind.Voice));

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("# --- クレジット該当なし ---\r\n# [音声] EngineA / f1\r\n", text);
    }

    [Fact]
    public void まとめ文が空なら該当なし()
    {
        Register(@"C:\a\se1.wav", MaterialKind.SoundEffect, "個別の文面", "出典");
        db.Sources.Add(new SourceSetting { Name = "出典", Merge = true, MergedCredit = " " });

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("# --- クレジット該当なし ---\r\n# [効果音] C:\\a\\se1.wav\r\n", text);
    }

    [Fact]
    public void 大文字小文字違いのパスで照合する()
    {
        db.Materials.Add(new MaterialEntry { KeyType = MaterialKeyType.File, Key = @"C:\A\b.wav", Kind = MaterialKind.SoundEffect, Credit = "SE" });
        used.Add(new UsedMaterial(MaterialKeyType.File, @"c:\a\B.wav", "B", MaterialKind.Other));

        var text = CreditBuilder.Build(used, db);

        Assert.Equal("■効果音\r\nSE\r\n", text);
    }

    [Fact]
    public void 素材が無ければ空文字列()
    {
        Assert.Equal("", CreditBuilder.Build(used, db));
    }
}
