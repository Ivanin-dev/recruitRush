using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>Portrait HUD and result screen (uGUI, built in code for the prototype).</summary>
    public class GameUI : MonoBehaviour
    {
        Text levelText, coinsText, weaponText, hintText, resultTitle, resultDetails, resultButtonLabel;
        GameObject resultPanel;
        RectTransform card;
        Button resultButton;

        public static GameUI Create()
        {
            var go = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var ui = go.AddComponent<GameUI>();
            ui.Build();
            return ui;
        }

        void Build()
        {
            var root = (RectTransform)transform;
            var safe = MakeRect(root, "SafeArea", Vector2.zero, Vector2.one);
            safe.gameObject.AddComponent<SafeArea>();

            levelText = MakeText(safe, "Level", new Vector2(0f, 1f), new Vector2(40f, -60f), new Vector2(600f, 90f), 64, TextAnchor.UpperLeft);
            weaponText = MakeText(safe, "Weapon", new Vector2(0f, 1f), new Vector2(40f, -140f), new Vector2(700f, 70f), 44, TextAnchor.UpperLeft);
            coinsText = MakeText(safe, "Coins", new Vector2(1f, 1f), new Vector2(-40f, -60f), new Vector2(500f, 90f), 58, TextAnchor.UpperRight);
            hintText = MakeText(safe, "Hint", new Vector2(0.5f, 0.2f), Vector2.zero, new Vector2(1000f, 120f), 66, TextAnchor.MiddleCenter);

            resultPanel = MakeRect(root, "Result", Vector2.zero, Vector2.one).gameObject;
            resultPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            card = MakeRect(resultPanel.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            card.sizeDelta = new Vector2(880f, 720f);
            card.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.16f, 0.12f, 0.96f);

            resultTitle = MakeText(card, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(840f, 120f), 84, TextAnchor.MiddleCenter);
            resultDetails = MakeText(card, "Details", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(800f, 220f), 52, TextAnchor.MiddleCenter);

            var buttonRect = MakeRect(card, "Button", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            buttonRect.anchoredPosition = new Vector2(0f, 140f);
            buttonRect.sizeDelta = new Vector2(540f, 160f);
            var image = buttonRect.gameObject.AddComponent<Image>();
            image.color = new Color(0.38f, 0.72f, 0.28f);
            resultButton = buttonRect.gameObject.AddComponent<Button>();
            resultButton.targetGraphic = image;
            resultButtonLabel = MakeText(buttonRect, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 150f), 70, TextAnchor.MiddleCenter);

            resultPanel.SetActive(false);
        }

        static RectTransform MakeRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        static Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(align is TextAnchor.UpperLeft ? 0f : align is TextAnchor.UpperRight ? 1f : 0.5f,
                align is TextAnchor.UpperLeft or TextAnchor.UpperRight ? 1f : 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = Art.Font;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.GetComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.6f);
            o.effectDistance = new Vector2(3f, -3f);
            return t;
        }

        public void SetLevel(int level) => levelText.text = "LEVEL " + level;
        public void SetCoins(int coins) => coinsText.text = coins + " ¢";
        public void SetWeapon(int tier) => weaponText.text = "WEAPON: " + Weapons.Names[tier];

        public void ShowHint(string text)
        {
            hintText.gameObject.SetActive(true);
            hintText.text = text;
        }

        public void HideHint() => hintText.gameObject.SetActive(false);

        public void ShowResult(bool win, string details, System.Action onClick)
        {
            resultPanel.SetActive(true);
            resultTitle.text = win ? "MISSION COMPLETE" : "MISSION FAILED";
            resultTitle.color = win ? new Color(0.6f, 1f, 0.45f) : new Color(1f, 0.45f, 0.4f);
            resultDetails.text = details;
            resultButtonLabel.text = win ? "NEXT" : "RETRY";
            resultButton.onClick.RemoveAllListeners();
            resultButton.onClick.AddListener(() => onClick());
            Pop.Play(card, 0.6f);
        }

        void Update()
        {
            if (hintText.gameObject.activeSelf)
                hintText.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.05f);
        }
    }

    /// <summary>Keeps HUD out of notches / rounded corners.</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect applied;

        void Update()
        {
            var area = Screen.safeArea;
            if (area == applied || Screen.width <= 0 || Screen.height <= 0) return;
            applied = area;
            var r = (RectTransform)transform;
            r.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            r.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        }
    }
}
