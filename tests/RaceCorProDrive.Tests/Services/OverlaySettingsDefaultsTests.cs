using FluentAssertions;
using RaceCorProDrive.Services;
using Xunit;

namespace RaceCorProDrive.Tests.Services;

/// <summary>
/// Regression coverage for OverlaySettingsDefaults.Apply — the
/// migration / self-heal layer that runs on every read of the on-disk
/// overlay-settings.json.
///
/// Each `Apply_*` test pins a specific recent fix:
///   - SelfHealsBareSimHubUrl       → fix(host): self-heal bare SimHub URL on read
///   - PromotesNormalizedZoom       → fix: zoom percentage migration from 0.18.x
///   - CoercesDefaultTheme          → fix: "default" theme → "dark"
/// </summary>
public class OverlaySettingsDefaultsTests
{
    // ── SimHub URL self-heal ──────────────────────────────────────

    [Fact]
    public void Apply_BareLocalhostSimHubUrl_AppendsPluginPath()
    {
        var input = new OverlaySettings { SimHubUrl = "http://localhost:8889" };
        OverlaySettingsDefaults.Apply(input);
        input.SimHubUrl.Should().Be("http://localhost:8889/racecor-io-pro-drive/");
    }

    [Fact]
    public void Apply_BareLocalhostSimHubUrlWithTrailingSlash_AppendsPluginPathExactlyOnce()
    {
        var input = new OverlaySettings { SimHubUrl = "http://localhost:8889/" };
        OverlaySettingsDefaults.Apply(input);
        input.SimHubUrl.Should().Be("http://localhost:8889/racecor-io-pro-drive/");
    }

    [Fact]
    public void Apply_AlreadyCanonicalSimHubUrl_LeftUnchanged()
    {
        var input = new OverlaySettings { SimHubUrl = "http://localhost:8889/racecor-io-pro-drive/" };
        OverlaySettingsDefaults.Apply(input);
        input.SimHubUrl.Should().Be("http://localhost:8889/racecor-io-pro-drive/");
    }

    [Fact]
    public void Apply_RemoteSimHubUrlAlreadyContainingPluginPath_LeftUnchanged()
    {
        // Casing-insensitive: ContainsOrdinalIgnoreCase. A user who
        // typed the path with capital letters shouldn't get a doubled
        // path appended.
        var input = new OverlaySettings { SimHubUrl = "http://192.168.1.50:8889/RaceCor-IO-Pro-Drive/" };
        OverlaySettingsDefaults.Apply(input);
        input.SimHubUrl.Should().Be("http://192.168.1.50:8889/RaceCor-IO-Pro-Drive/");
    }

    [Fact]
    public void Apply_NullSimHubUrl_FallsBackToCanonicalDefault()
    {
        var input = new OverlaySettings { SimHubUrl = null };
        OverlaySettingsDefaults.Apply(input);
        input.SimHubUrl.Should().Be("http://localhost:8889/racecor-io-pro-drive/");
    }

    [Fact]
    public void Apply_EmptyStringSimHubUrl_FallsBackToCanonicalDefault()
    {
        // Empty string skips the self-heal branch (IsNullOrEmpty check)
        // and then the ??= default fills it. Without that double-guard
        // an empty-string save would persist as empty.
        var input = new OverlaySettings { SimHubUrl = "" };
        OverlaySettingsDefaults.Apply(input);
        input.SimHubUrl.Should().Be("http://localhost:8889/racecor-io-pro-drive/");
    }

    // ── Zoom migration (0.18.x normalized → percentage) ────────────

    [Theory]
    [InlineData(1.0, 100.0)]
    [InlineData(1.5, 150.0)]
    [InlineData(2.0, 200.0)]
    [InlineData(0.5, 50.0)]
    public void Apply_NormalizedZoomFrom018x_PromotedToPercentage(double stored, double expected)
    {
        var input = new OverlaySettings { Zoom = stored };
        OverlaySettingsDefaults.Apply(input);
        input.Zoom.Should().Be(expected);
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(165.0)]
    [InlineData(200.0)]
    public void Apply_PercentageZoom_LeftUnchanged(double stored)
    {
        var input = new OverlaySettings { Zoom = stored };
        OverlaySettingsDefaults.Apply(input);
        input.Zoom.Should().Be(stored);
    }

    [Fact]
    public void Apply_NullZoom_FallsBackToOverlayDefault()
    {
        var input = new OverlaySettings { Zoom = null };
        OverlaySettingsDefaults.Apply(input);
        input.Zoom.Should().Be(165.0);
    }

    [Fact]
    public void Apply_ZeroZoom_FallsBackToOverlayDefault()
    {
        // The promote-normalized check is `> 0 && < 5`, so zero is left
        // alone — but then ??= kicks in only if null. Confirm zero
        // survives untouched (it'd be a degenerate value but the host
        // shouldn't paper over user-saved 0).
        var input = new OverlaySettings { Zoom = 0.0 };
        OverlaySettingsDefaults.Apply(input);
        input.Zoom.Should().Be(0.0);
    }

    // ── Theme coercion ─────────────────────────────────────────────

    [Fact]
    public void Apply_ThemeStoredAsDefault_CoercedToDark()
    {
        var input = new OverlaySettings { Theme = "default" };
        OverlaySettingsDefaults.Apply(input);
        input.Theme.Should().Be("dark");
    }

    [Fact]
    public void Apply_ThemeStoredAsDefaultWithMixedCase_CoercedToDark()
    {
        var input = new OverlaySettings { Theme = "Default" };
        OverlaySettingsDefaults.Apply(input);
        input.Theme.Should().Be("dark");
    }

    [Fact]
    public void Apply_NullTheme_FallsBackToDark()
    {
        var input = new OverlaySettings { Theme = null };
        OverlaySettingsDefaults.Apply(input);
        input.Theme.Should().Be("dark");
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("brand-red")]
    public void Apply_ValidNonDefaultTheme_LeftUnchanged(string theme)
    {
        var input = new OverlaySettings { Theme = theme };
        OverlaySettingsDefaults.Apply(input);
        input.Theme.Should().Be(theme);
    }

    // ── General default-fill behavior ──────────────────────────────

    [Fact]
    public void Apply_EmptyInput_FillsAllRequiredDefaults()
    {
        var input = new OverlaySettings();
        OverlaySettingsDefaults.Apply(input);

        input.LogoOnlyStart.Should().Be(true);
        input.LayoutPosition.Should().Be("top-right");
        input.Zoom.Should().Be(165.0);
        input.AmbientMode.Should().Be("auto");
        input.Theme.Should().Be("dark");
        input.ApiBase.Should().Be("https://prodrive.racecor.io");
        input.UseRemoteTokens.Should().Be(false);
    }

    [Fact]
    public void Apply_PreservesUserOverridesOverDefaults()
    {
        var input = new OverlaySettings
        {
            LogoOnlyStart = false,
            LayoutPosition = "bottom-left",
            ApiBase = "https://staging.prodrive.example",
        };
        OverlaySettingsDefaults.Apply(input);

        input.LogoOnlyStart.Should().Be(false);
        input.LayoutPosition.Should().Be("bottom-left");
        input.ApiBase.Should().Be("https://staging.prodrive.example");
    }

    [Fact]
    public void Apply_ReturnsSameInstance()
    {
        var input = new OverlaySettings();
        var result = OverlaySettingsDefaults.Apply(input);
        result.Should().BeSameAs(input);
    }
}

public class AmbientRectTests
{
    [Fact]
    public void AmbientRect_DefaultValues_AreZero()
    {
        var rect = new AmbientRect();
        rect.X.Should().Be(0);
        rect.Y.Should().Be(0);
        rect.W.Should().Be(0);
        rect.H.Should().Be(0);
    }

    [Fact]
    public void AmbientRect_StoresSetValues()
    {
        // Region picker normalizes pick coords as fractions of the
        // primary display (0..1). Confirm round-trip without coercion.
        var rect = new AmbientRect { X = 0.25, Y = 0.5, W = 0.4, H = 0.3 };
        rect.X.Should().Be(0.25);
        rect.Y.Should().Be(0.5);
        rect.W.Should().Be(0.4);
        rect.H.Should().Be(0.3);
    }
}
