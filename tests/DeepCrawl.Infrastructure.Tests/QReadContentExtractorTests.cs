using DeepCrawl.Infrastructure.Cleaning;
using Xunit;

namespace DeepCrawl.Infrastructure.Tests;

/// <summary>
/// Unit tests for QReadContentExtractor — verifies paragraph-density-based
/// content extraction for Chinese web pages.
/// </summary>
public class QReadContentExtractorTests
{
    private readonly QReadContentExtractor _extractor = new();

    private static string Html(string body) =>
        $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>{body}</body></html>";

    [Fact]
    public void Extract_TypicalChineseNews_ReturnsArticleBody()
    {
        var input = Html("""
            <nav><a href="/">首页</a> <a href="/news">新闻</a> <a href="/sports">体育</a></nav>
            <article>
                <h1>中国航天员完成太空行走任务</h1>
                <p>北京时间6月20日消息，中国载人航天工程办公室宣布，神舟二十号航天员乘组于今日成功完成了一次约7小时的出舱活动。</p>
                <p>此次出舱活动由指令长张伟和航天员李明共同执行，主要完成了空间站外部设备安装和巡检任务。在舱外机械臂的辅助下，两名航天员密切配合，顺利完成了全部既定任务。</p>
                <p>中国载人航天工程办公室表示，此次任务的成功实施，标志着中国空间站运营阶段的出舱活动技术日益成熟，为后续更大规模的空间科学实验奠定了基础。</p>
                <p>据悉，神舟二十号乘组自入驻空间站以来，已累计开展各类科学实验和技术试验三十余项，取得了丰硕成果。</p>
            </article>
            <footer>版权所有 © 2026 新华网</footer>
            """);

        var result = _extractor.Extract(input, "中国航天员完成太空行走任务");

        Assert.True(result.Extracted);
        Assert.True(result.OutputHtml.Length < input.Length);
        Assert.Contains("航天员", result.OutputHtml);
        Assert.Contains("出舱活动", result.OutputHtml);
        Assert.DoesNotContain("首页", result.OutputHtml);
        Assert.DoesNotContain("版权所有", result.OutputHtml);
    }

    [Fact]
    public void Extract_MultipleContainers_SelectsHighestDensity()
    {
        var input = Html("""
            <div class="sidebar">
                <h3>热门推荐</h3>
                <ul>
                    <li><a href="/a1">短标题</a></li>
                    <li><a href="/a2">另一个</a></li>
                    <li><a href="/a3">推荐</a></li>
                </ul>
            </div>
            <div class="content">
                <p>深度学习是机器学习的一个重要分支，它通过构建多层神经网络来学习数据的层次化表示。近年来，随着计算能力的提升和大规模数据集的出现，深度学习在图像识别、自然语言处理、语音识别等领域取得了突破性进展。transformer 架构的出现更是彻底改变了自然语言处理的研究范式，使得大规模语言模型成为可能。</p>
                <p>在计算机视觉领域，卷积神经网络仍然是主流架构之一，但 Vision Transformer 正在迅速追赶。研究人员发现，当训练数据足够大时，Transformer 模型能够超越传统的 CNN 模型。</p>
            </div>
            """);

        var result = _extractor.Extract(input, null);

        Assert.True(result.Extracted);
        Assert.Contains("深度学习", result.OutputHtml);
        Assert.Contains("transformer", result.OutputHtml);
        Assert.DoesNotContain("热门推荐", result.OutputHtml);
    }

    [Fact]
    public void Extract_NavigationWithManyShortLines_IsPenalized()
    {
        var input = Html("""
            <nav>
                <ul>
                    <li>首页</li><li>新闻</li><li>财经</li><li>体育</li>
                    <li>娱乐</li><li>科技</li><li>汽车</li><li>房产</li>
                    <li>教育</li><li>健康</li><li>旅游</li><li>美食</li>
                    <li>游戏</li><li>军事</li><li>历史</li><li>文化</li>
                </ul>
            </nav>
            <div>
                <p>今天天气很好，适合出去走走。春天的阳光温暖而不灼热，微风轻拂着脸庞，带来了花香和青草的气息。公园里的樱花开了，粉白色的花瓣在阳光下显得格外娇嫩。</p>
            </div>
            """);

        var result = _extractor.Extract(input, null);

        Assert.True(result.Extracted);
        Assert.Contains("今天天气很好", result.OutputHtml);
        Assert.DoesNotContain("军事", result.OutputHtml);
    }

    [Fact]
    public void Extract_NoiseClassNames_AreHeavilyPenalized()
    {
        var input = Html("""
            <div class="sidebar widget">
                <p>这是一段很长的文本内容放在侧边栏里，它可能包含了很多有用的信息，但是因为它位于侧边栏这种辅助区域中，所以不应该被选为正文内容提取的目标区域。</p>
            </div>
            <div class="article-body">
                <p>人工智能技术的发展正在深刻改变着我们的生活方式和工作模式。从智能手机中的语音助手，到自动驾驶汽车，再到医疗影像诊断系统，人工智能的应用已经渗透到社会的方方面面。专家预测，未来十年内，人工智能将在教育、医疗、金融等领域带来更加革命性的变化。</p>
            </div>
            """);

        var result = _extractor.Extract(input, null);

        Assert.True(result.Extracted);
        Assert.Contains("人工智能", result.OutputHtml);
        Assert.DoesNotContain("侧边栏", result.OutputHtml);
    }

    [Fact]
    public void Extract_HighLinkDensity_IsPenalized()
    {
        var input = Html("""
            <div class="link-list">
                <ul>
                    <li><a href="/1">相关文章标题一比较长</a></li>
                    <li><a href="/2">相关文章标题二也很长</a></li>
                    <li><a href="/3">相关文章标题三更长一点</a></li>
                    <li><a href="/4">相关文章标题四同样长度</a></li>
                    <li><a href="/5">相关文章标题五继续推荐</a></li>
                </ul>
            </div>
            <div class="main-text">
                <p>蛋白质是生命活动的主要承担者，它们参与了细胞中几乎所有的生物学过程。蛋白质的功能取决于其三维结构，而三维结构又由氨基酸序列决定。近年来，基于深度学习的蛋白质结构预测方法取得了重大突破，AlphaFold 等模型能够以接近实验精度的水平预测蛋白质的三维结构。</p>
            </div>
            """);

        var result = _extractor.Extract(input, null);

        Assert.True(result.Extracted);
        Assert.Contains("蛋白质", result.OutputHtml);
        Assert.DoesNotContain("相关文章标题", result.OutputHtml);
    }

    [Fact]
    public void Extract_EmptyHtml_ReturnsInput()
    {
        var result = _extractor.Extract("", null);
        Assert.False(result.Extracted);
        Assert.Equal("", result.OutputHtml);

        result = _extractor.Extract("   \n  \t  ", null);
        Assert.False(result.Extracted);
    }

    [Fact]
    public void Extract_NoGoodCandidate_ReturnsInput()
    {
        var input = Html("""
            <nav><a href="/">首页</a></nav>
            <footer>版权信息</footer>
            """);

        var result = _extractor.Extract(input, null);

        Assert.False(result.Extracted);
        Assert.Equal(input, result.OutputHtml);
    }

    [Fact]
    public void Extract_TitleSimilarity_BoostsRelevantContent()
    {
        var input = Html("""
            <div>
                <p>今天天气不错，适合出门游玩。公园里有很多人放风筝。</p>
            </div>
            <div>
                <p>量子计算是一种利用量子力学原理进行信息处理的新型计算范式。与传统计算机使用比特（0或1）不同，量子计算机使用量子比特，可以同时处于0和1的叠加态。这种特性使得量子计算机在解决某些特定问题时具有指数级的加速优势，例如大数质因数分解、量子系统模拟和优化问题。</p>
            </div>
            """);

        var result = _extractor.Extract(input, "量子计算原理与应用前景");

        Assert.True(result.Extracted);
        Assert.Contains("量子计算", result.OutputHtml);
        Assert.DoesNotContain("放风筝", result.OutputHtml);
    }

    [Fact]
    public void Extract_ChineseShortParagraphs_AreNotFiltered()
    {
        var input = Html("""
            <nav><a href="/">首页</a></nav>
            <article>
                <p>她看着他，轻轻地说："你来了。"</p>
                <p>他点点头，目光深邃而坚定。</p>
                <p>"我等了三年。"她的声音有些颤抖。</p>
                <p>"我知道。"他缓缓走近，伸出手臂将她拥入怀中。</p>
                <p>窗外的雨声渐渐小了，一缕阳光穿透云层，照在两人的身上。这一刻，所有的等待都变得值得。</p>
            </article>
            <footer>Copyright 2026</footer>
            """);

        var result = _extractor.Extract(input, "三年等待");

        Assert.True(result.Extracted);
        Assert.Contains("你来了", result.OutputHtml);
        Assert.Contains("等了三年", result.OutputHtml);
        Assert.Contains("窗外的雨", result.OutputHtml);
        Assert.DoesNotContain("首页", result.OutputHtml);
        Assert.DoesNotContain("Copyright", result.OutputHtml);
    }

    [Fact]
    public void Extract_DeeplyNestedContent_FindsContent()
    {
        var input = Html("""
            <div class="wrapper">
                <div class="container">
                    <div class="layout">
                        <div class="main-col">
                            <div class="post">
                                <div class="post-body">
                                    <p>改革开放以来，中国经济取得了举世瞩目的成就。从1978年到2025年，中国国内生产总值从3679亿元增长到超过130万亿元，人均GDP从不足200美元提高到超过1.3万美元。这一发展速度在世界经济史上是罕见的，数亿人口摆脱了贫困，人民生活水平得到了显著提高。</p>
                                    <p>中国经济的快速发展得益于多方面因素，包括市场化改革、对外开放政策、基础设施建设、人力资本投资以及科技创新。近年来，中国在数字经济、新能源、人工智能等新兴领域的发展尤为突出。</p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            """);

        var result = _extractor.Extract(input, "中国经济发展成就");

        Assert.True(result.Extracted);
        Assert.Contains("改革开放", result.OutputHtml);
        Assert.Contains("国内生产总值", result.OutputHtml);
    }
}
