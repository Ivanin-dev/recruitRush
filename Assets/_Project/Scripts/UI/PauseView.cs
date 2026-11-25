using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>Pause card: two big toggles (sound, vibration), RESUME and RESTART.</summary>
    public class PauseView : MonoBehaviour
    {
        const string SoundKey = "rr_sound", VibrationKey = "rr_vibration";

        RectTransform card;
        TitleText title;
        TactileButton resume, restart;
        (Block pill, TextMeshProUGUI text) sound, vibration;
        Action onResume, onRestart;

        public static PauseView Create(RectTransform safe, Action onResume, Action onRestart)
        {
            Sfx.Muted = PlayerPrefs.GetInt(SoundKey, 1) == 0;
            Haptics.Enabled = PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            var r = UiKit.Stretch(safe, "Pause");
            var v = r.gameObject.AddComponent<PauseView>();
            v.onResume = onResume;
            v.onRestart = onRestart;
            v.Build(r);
            return v;
        }

        void Build(RectTransform r)
        {
            var th = UiTheme.Current;
            var dimR = UiKit.Stretch(r, "Dim");
            dimR.anchorMin = new Vector2(-0.5f, -0.5f);
            dimR.anchorMax = new Vector2(1.5f, 1.5f);
            // Lobby style: a light dim (the scene stays visible) and no big panel: the title floats, every control is its
            // own chunky lipped tile in the lobby's slate.
            var d = th.dim;
            dimR.gameObject.AddComponent<Image>().color = new Color(d.r, d.g, d.b, 0.42f); // also swallows taps

            card = UiKit.Rect(r, "Controls", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(760f, 900f));
            var c = card;
            title = UiKit.Title(c, "Title", "PAUSED", 96f, th.titleSilverTop, th.titleSilverBottom);
            title.root.anchorMin = new Vector2(0f, 1f);
            title.root.offsetMin = new Vector2(0f, -170f);
            title.root.offsetMax = new Vector2(0f, -40f);

            sound = Toggle(c, "SOUND", -250f, ToggleSound);
            vibration = Toggle(c, "VIBRATION", -390f, ToggleVibration);
            Refresh();

            resume = TactileButton.Create(c, "Resume", new Vector2(0.5f, 0f), new Vector2(0f, 270f), new Vector2(580f, 150f),
                th.green, th.greenDark, "RESUME", 76f, () => onResume?.Invoke());
            restart = TactileButton.Create(c, "Restart", new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(580f, 120f),
                th.slateFace, th.slateLip, "RESTART", 58f, () => onRestart?.Invoke(), 12f);
            gameObject.SetActive(false);
        }

        (Block, TextMeshProUGUI) Toggle(RectTransform parent, string name, float y, Action onClick)
        {
            var th = UiTheme.Current;
            var b = TactileButton.Create(parent, name, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(600f, 110f),
                th.slateFace, th.slateLip, null, 0f, onClick, 10f);
            var label = UiKit.Label(b.content, "Label", name, 50f, th.textMain, TextAlignmentOptions.MidlineLeft);
            label.rectTransform.offsetMin = new Vector2(36f, 0f);
            var pill = UiKit.Lipped(b.content, "State", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 3f), new Vector2(150f, 70f),
                th.green, th.greenDark, 7f, 35f);
            var text = UiKit.Label(pill.face, "Text", "ON", 44f, th.textMain);
            return (pill, text);
        }

        /// <param name="settings">Lobby settings: same toggles, titled SETTINGS, no RESTART, CLOSE instead of RESUME.</param>
        public void Show(bool settings = false)
        {
            title.Text = settings ? "SETTINGS" : "PAUSED";
            resume.label.text = settings ? "CLOSE" : "RESUME";
            restart.gameObject.SetActive(!settings);
            ((RectTransform)resume.transform).anchoredPosition = new Vector2(0f, settings ? 150f : 270f);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
            Pop.Play(card, 0.8f);
        }

        public void Hide()
        {
            PlayerPrefs.Save();
            gameObject.SetActive(false);
        }

        void Refresh()
        {
            Set(sound, !Sfx.Muted);
            Set(vibration, Haptics.Enabled);
        }

        static void Set((Block pill, TextMeshProUGUI text) t, bool on)
        {
            var th = UiTheme.Current;
            t.pill.SetColors(on ? th.green : th.red, on ? th.greenDark : th.redDark);
            UiKit.InkLabel(t.text, on ? th.greenDark : th.redDark);
            t.text.text = on ? "ON" : "OFF";
        }

        void ToggleSound()
        {
            Sfx.Muted = !Sfx.Muted;
            PlayerPrefs.SetInt(SoundKey, Sfx.Muted ? 0 : 1);
            Refresh();
            Pop.Play(sound.pill.root, 1.25f);
        }

        void ToggleVibration()
        {
            Haptics.Enabled = !Haptics.Enabled;
            PlayerPrefs.SetInt(VibrationKey, Haptics.Enabled ? 1 : 0);
            if (Haptics.Enabled) Haptics.Medium();
            Refresh();
            Pop.Play(vibration.pill.root, 1.25f);
        }
    }
}
