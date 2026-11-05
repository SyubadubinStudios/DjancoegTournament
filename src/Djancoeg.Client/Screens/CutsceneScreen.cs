using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>
/// Story still: the image drifts slowly (Ken Burns), letterbox bars, captions typed out line by line.
/// Enter / click / Space advances, Esc skips the whole scene.
/// </summary>
public sealed class CutsceneScreen(App app, Cutscene scene, Func<Screen> next) : Screen(app)
{
    private float _t, _lineT;
    private int _line;
    private bool _done;
    private const float CharsPerSecond = 42f;

    public override bool ReadyForScreenshot => _t > 4.5f;
    private int _voicedLine = -1;

    /// <summary>Narration: assets/audio/story/&lt;scene&gt;-&lt;line&gt;.mp3 (Indonesian voice; captions follow the UI language).</summary>
    private void Narrate()
    {
        if (_voicedLine == _line || App.Options.Shot is not null) return;
        _voicedLine = _line;
        App.Sounds.PlayStory($"{scene.Id}-{_line + 1}.mp3", App.Settings.StoryVoice ? 1f : 0f);
    }

    public override void Leave() => App.Sounds.StopStory();

    public override void Update(float dt)
    {
        _t += dt;
        _lineT += dt;
        if (_done) return;
        if (_t > 0.4f && App.Settings.StoryVoice) Narrate();
        if (Gui.Back) { Finish(); return; }
        bool advance = Gui.Confirm || IsMouseButtonPressed(MouseButton.Left) || IsKeyPressed(KeyboardKey.Space);
        if (!advance) return;
        string text = CurrentText();
        if (_lineT * CharsPerSecond < text.Length) { _lineT = 999; return; } // first press finishes typing
        if (_line + 1 < scene.Lines.Count) { _line++; _lineT = 0; App.Sounds.Play(Audio.Sfx.UiMove, 0.4f); }
        else Finish();
    }

    private void Finish()
    {
        _done = true;
        App.Go(next());
    }

    private string CurrentText() => scene.Lines.Count == 0 ? "" : S.T(scene.Lines[_line]);

    public override void Draw()
    {
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;
        ClearBackground(Theme.Aspal);
        if (App.Models.Texture(scene.Image) is { } tex)
        {
            // Cover the screen, then slowly zoom in and drift sideways.
            float zoom = 1.04f + _t * 0.012f;
            float scale = MathF.Max((float)sw / tex.Width, (float)sh / tex.Height) * zoom;
            float w = tex.Width * scale, h = tex.Height * scale;
            float driftX = (w - sw) * (0.5f + 0.4f * MathF.Sin(_t * 0.08f)), driftY = (h - sh) * 0.5f;
            DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), new Rectangle(-driftX, -driftY, w, h), Vector2.Zero, 0, Color.White);
        }
        float fadeIn = MathF.Min(1, _t * 1.5f);
        if (fadeIn < 1) DrawRectangle(0, 0, sw, sh, Theme.Fade(Theme.Aspal, 1 - fadeIn));

        float bar = 110 * u;
        DrawRectangle(0, 0, sw, (int)bar, Theme.Aspal);
        DrawRectangle(0, sh - (int)(bar * 1.5f), sw, (int)(bar * 1.5f), Theme.Fade(Theme.Aspal, 0.92f));
        Theme.SignText(S.T(scene.Title).ToUpperInvariant(), new Vector2(60 * u, 30 * u), 52 * u, Theme.Kuning, doubleShadow: true);

        string text = CurrentText();
        int shown = Math.Min(text.Length, (int)(_lineT * CharsPerSecond));
        Theme.Paragraph(Theme.BodyBold, text[..shown], new Vector2(80 * u, sh - bar * 1.5f + 30 * u), 30 * u, sw - 160 * u, Theme.Kapur);
        string hint = $"{_line + 1}/{Math.Max(1, scene.Lines.Count)}   ·   {S.T("cut.hint")}";
        Theme.TextRight(Theme.Mono, hint, new Vector2(sw - 40 * u, sh - 40 * u), 16 * u, Theme.Fade(Theme.Kapur, 0.7f));
        Theme.Text(Theme.Mono, Core.GameInfo.CreditId, new Vector2(40 * u, sh - 40 * u), 14 * u, Theme.Fade(Theme.Kapur, 0.45f));
    }
}
