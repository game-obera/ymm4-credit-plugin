namespace YMM4CreditPlugin.Core;

/// <summary>ymmp から抽出した、使用中の素材 1 つ。</summary>
/// <param name="KeyType">キーの種類</param>
/// <param name="Key">照合キー</param>
/// <param name="DefaultName">素材名の初期値</param>
/// <param name="GuessedKind">推定した素材種類</param>
public sealed record UsedMaterial(MaterialKeyType KeyType, string Key, string DefaultName, MaterialKind GuessedKind);
