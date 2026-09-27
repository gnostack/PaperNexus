using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

// The app's GitHub owner/name comes from the RepositoryUrl assembly metadata, which the build
// takes from the checkout's git origin. These cover every origin form git produces and the
// refusal of values that would send update checks or bug reports to the wrong place.
public class GitHubRepositoryTests
{
    [Theory]
    [InlineData("https://github.com/gnostack/PaperNexus")]
    [InlineData("https://github.com/gnostack/PaperNexus.git")]
    [InlineData("https://github.com/gnostack/PaperNexus/")]
    [InlineData("git@github.com:gnostack/PaperNexus.git")]
    [InlineData("git@github.com:gnostack/PaperNexus")]
    [InlineData("ssh://git@github.com/gnostack/PaperNexus.git")]
    [InlineData("ssh://git@github.com/gnostack/PaperNexus")]
    public void Parse_ReadsOwnerAndName_FromEveryOriginForm(string repositoryUrl)
    {
        var repository = GitHubRepository.Parse(repositoryUrl);

        Assert.Equal("gnostack", repository.Owner);
        Assert.Equal("PaperNexus", repository.Name);
        Assert.Equal("gnostack/PaperNexus", repository.Slug);
        Assert.Equal("https://github.com/gnostack/PaperNexus", repository.WebUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_RefusesAMissingValue(string? repositoryUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GitHubRepository.Parse(repositoryUrl));
        Assert.Contains("RepositoryUrl", ex.Message);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://github.com/gnostack")]
    [InlineData("https://github.com/gnostack/PaperNexus/issues")]
    [InlineData("https://gitlab.com/gnostack/PaperNexus.git")]
    [InlineData("git@gitlab.com:gnostack/PaperNexus.git")]
    [InlineData("https://github.com/gnostack/.git")]
    [InlineData("/home/justin/repos/PaperNexus")]
    [InlineData("file:///home/justin/repos/PaperNexus")]
    public void Parse_RefusesAMalformedValue(string repositoryUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GitHubRepository.Parse(repositoryUrl));
        Assert.Contains(repositoryUrl, ex.Message);
    }

    [Fact]
    public void FromAssembly_RefusesAnAssemblyWithoutTheMetadata()
    {
        // The test assembly is not built with the embedded repository URL.
        var testAssembly = typeof(GitHubRepositoryTests).Assembly;

        Assert.Throws<InvalidOperationException>(() => GitHubRepository.FromAssembly(testAssembly));
    }

    [Fact]
    public void Current_ResolvesFromTheBuiltAssembly()
    {
        // The app assembly carries whatever origin this checkout has, so only its shape is
        // asserted: a non-empty owner and name on github.com.
        var repository = GitHubRepository.Current;

        Assert.False(string.IsNullOrEmpty(repository.Owner));
        Assert.False(string.IsNullOrEmpty(repository.Name));
        Assert.StartsWith("https://github.com/", repository.WebUrl);
    }
}
