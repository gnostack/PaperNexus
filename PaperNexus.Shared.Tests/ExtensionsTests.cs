using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

public class ExtensionsTests
{
    // ThrowIfNull should return the value unchanged when it is not null.
    [Fact]
    public void ThrowIfNull_NonNullValue_ReturnsValue()
    {
        var expected = "hello";
        var result = expected.ThrowIfNull();
        Assert.Equal(expected, result);
    }

    // The CallerArgumentExpression attribute captures the actual argument expression
    // at the call site. When null is passed as a named local variable, the exception's
    // ParamName must reflect that variable name — not the generic "value" used internally.
    [Fact]
    public void ThrowIfNull_NullValue_ExceptionParamNameMatchesCallSiteExpression()
    {
        var myNullVariable = (string)null;
        var ex = Assert.Throws<ArgumentNullException>(() => myNullVariable.ThrowIfNull());
        // CallerArgumentExpression captures "myNullVariable", not "value"
        Assert.Equal("myNullVariable", ex.ParamName);
    }

    // When a cast expression is passed, CallerArgumentExpression captures the full
    // expression text. The point is simply that "value" is never the ParamName.
    [Fact]
    public void ThrowIfNull_NullLiteral_ParamNameIsNotTheGenericValueName()
    {
        var nullString = (string)null;
        var ex = Assert.Throws<ArgumentNullException>(() => nullString.ThrowIfNull());
        Assert.NotEqual("value", ex.ParamName);
    }
}
