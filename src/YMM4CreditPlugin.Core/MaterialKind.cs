namespace YMM4CreditPlugin.Core;

/// <summary>素材の種類。宣言順がクレジットの出力順になる。</summary>
public enum MaterialKind
{
    Tachie,
    Bgm,
    SoundEffect,
    Image,
    Video,
    Voice,
    Font,
    Other,
}

public static class MaterialKindExtensions
{
    public static string ToDisplayName(this MaterialKind kind) => kind switch
    {
        MaterialKind.Tachie => "立ち絵",
        MaterialKind.Bgm => "BGM",
        MaterialKind.SoundEffect => "効果音",
        MaterialKind.Image => "画像",
        MaterialKind.Video => "動画",
        MaterialKind.Voice => "音声",
        MaterialKind.Font => "フォント",
        _ => "その他",
    };
}

/// <summary>素材を照合するときのキーの種類。</summary>
public enum MaterialKeyType
{
    /// <summary>ファイル(キーは絶対パス)</summary>
    File,
    /// <summary>立ち絵(キーはキャラ素材フォルダの絶対パス)</summary>
    Tachie,
    /// <summary>合成音声(キーは "API|Arg")</summary>
    Voice,
    /// <summary>フォント(キーはフォント名)</summary>
    Font,
}

public static class MaterialKey
{
    /// <summary>素材キーの比較方法。パスは大文字小文字を区別しない。</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;
}
