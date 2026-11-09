using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin.Core.Tests;

public class MaterialKindTests
{
    [Fact]
    public void 表示名が仕様どおり()
    {
        Assert.Equal(
            new[] { "立ち絵", "BGM", "効果音", "画像", "動画", "音声", "フォント", "その他" },
            Enum.GetValues<MaterialKind>().Select(k => k.ToDisplayName()));
    }
}
