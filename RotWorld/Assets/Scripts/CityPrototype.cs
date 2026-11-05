using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RotWorld
{
    /// <summary>
    /// 기본 Unity UI를 통한 도시 생성 설정과 결과 연결
    /// </summary>
    public sealed class CityPrototype : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _koreanFont;
        [SerializeField] private TMP_FontAsset _englishFont;
        [SerializeField] private Camera _worldCamera;
        private static readonly Color PanelColor = new Color32(35, 43, 52, 255);
        private static readonly Color ControlColor = new Color32(57, 69, 80, 255);
        private CityMapView _view;
        private GameObject _dialog;
        private TMP_Text _status;
        private TMP_Text _summary;
        private TMP_Text _welcome;
        private TMP_InputField _seed;
        private TMP_InputField _humans;
        private TMP_InputField _zombies;
        private Button _generate;
        private Coroutine _generation;
        private int _sizeIndex = 1;
        private int _densityIndex = 1;
        private int _supplyIndex = 1;
        private readonly int[] _sizes = { 128, 256, 512 };
        private readonly int[] _densities = { 40, 65, 85 };
        private readonly int[] _supplies = { 50, 100, 150 };

        // 필수 씬 참조 검증과 시제품 구성
        private void Start()
        {
            if (_koreanFont == null || _englishFont == null || _worldCamera == null)
            {
                Debug.LogError("도시 시제품의 카메라 또는 neodgm SDF 폰트 참조가 없습니다", this);
                enabled = false;
                return;
            }
            var mapObject = new GameObject("Generated City");
            mapObject.transform.SetParent(transform, false);
            _view = mapObject.AddComponent<CityMapView>();
            _view.Initialize(_worldCamera);
            BuildInterface();
        }

        // 직사각형 기본 UI 오브젝트 생성
        private static RectTransform Rect(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        // 장식 이미지 없는 단색 배경 생성
        private static Image Background(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        // 언어별 SDF 폰트를 적용한 텍스트 생성
        private TMP_Text Label(Transform parent, string text, Vector2 position, Vector2 size,
            float fontSize = 18, bool english = false)
        {
            var rect = Rect(text, parent, new Vector2(0, 1), new Vector2(0, 1), position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = english ? _englishFont : _koreanFont;
            label.text = text;
            label.fontSize = fontSize;
            label.color = new Color32(235, 234, 216, 255);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        // 기본 색 전환과 텍스트를 가진 버튼 생성
        private Button MakeButton(Transform parent, string text, Vector2 position, Vector2 size,
            UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(text, parent, new Vector2(0, 1), new Vector2(0, 1), position, size);
            var image = Background(rect, ControlColor);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.78f, .86f, .9f);
            colors.pressedColor = new Color(.55f, .65f, .7f);
            button.colors = colors;
            var label = Label(rect, text, Vector2.zero, size);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(action);
            return button;
        }

        // 숫자 입력과 명시적 범위 안내
        private TMP_InputField NumberField(Transform parent, string title, string initial,
            int row, bool signed = false)
        {
            float y = -112 - row * 48;
            Label(parent, title, new Vector2(24, y), new Vector2(160, 36));
            var rect = Rect(title + " 입력", parent, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(198, y), new Vector2(244, 36));
            Background(rect, ControlColor);
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect("Viewport", rect, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(10, -2), new Vector2(224, 32));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label(viewport, initial, Vector2.zero, new Vector2(224, 32), 18, true);
            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)text;
            input.contentType = signed ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Custom;
            if (!signed) { input.characterValidation = TMP_InputField.CharacterValidation.Digit; }
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 11;
            input.text = initial;
            return input;
        }

        // 열거형 설정 행과 선택 버튼 생성
        private void Choice(Transform parent, string title, int row, string[] names, int initial,
            Action<int> changed)
        {
            float y = -112 - row * 48;
            Label(parent, title, new Vector2(24, y), new Vector2(160, 36));
            int index = initial;
            TMP_Text value = null;
            MakeButton(parent, "이전", new Vector2(198, y), new Vector2(48, 36), () =>
            {
                index = (index + names.Length - 1) % names.Length;
                value.text = names[index];
                changed(index);
            });
            value = Label(parent, names[index], new Vector2(246, y), new Vector2(148, 36), 16);
            value.alignment = TextAlignmentOptions.Center;
            MakeButton(parent, "다음", new Vector2(394, y), new Vector2(48, 36), () =>
            {
                index = (index + 1) % names.Length;
                value.text = names[index];
                changed(index);
            });
        }

        // 하단 도구 모음과 생성 설정창 구성
        private void BuildInterface()
        {
            var canvasRect = Rect("City UI", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            var bar = Rect("Bottom Toolbar", canvasRect, new Vector2(0, 0), Vector2.zero,
                Vector2.zero, new Vector2(0, 64));
            bar.anchorMax = new Vector2(1, 0);
            Background(bar, PanelColor);
            _summary = Label(bar, "새로운 도시를 만들어 주세요", new Vector2(22, -12), new Vector2(720, 40), 16);
            var tools = Rect("Map Tools", bar, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-18, -10), new Vector2(438, 44));
            MakeButton(tools, "축소", Vector2.zero, new Vector2(62, 44), () => _view.ChangeZoom(-1));
            MakeButton(tools, "확대", new Vector2(68, 0), new Vector2(62, 44), () => _view.ChangeZoom(1));
            MakeButton(tools, "중앙", new Vector2(136, 0), new Vector2(62, 44), () => _view.Recenter());
            MakeButton(tools, "맵 생성하기", new Vector2(214, 0), new Vector2(224, 44), OpenDialog);
            _welcome = Label(canvasRect, "도시의 시작\n\n우측 하단의 맵 생성하기를 눌러 주세요",
                new Vector2(0, 0), new Vector2(600, 120), 24);
            var welcomeRect = _welcome.rectTransform;
            welcomeRect.anchorMin = welcomeRect.anchorMax = new Vector2(.5f, .5f);
            welcomeRect.pivot = new Vector2(.5f, .5f);
            _welcome.alignment = TextAlignmentOptions.Center;
            var dialog = Rect("Map Generation", canvasRect, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-18, 78), new Vector2(466, 526));
            _dialog = dialog.gameObject;
            Background(dialog, PanelColor);
            Label(dialog, "새로운 세계", new Vector2(24, -16), new Vector2(350, 40), 25);
            Label(dialog, "도심·시가지·마을·강변·산업이 있는 섬", new Vector2(24, -62), new Vector2(418, 32), 16);
            Choice(dialog, "맵 크기", 0, new[] { "소형 128", "중형 256", "대형 512" }, 1, i => _sizeIndex = i);
            _seed = NumberField(dialog, "시드", "12345", 1, true);
            Choice(dialog, "개발 밀도", 2, new[] { "낮음 · 40%", "기본 · 65%", "높음 · 85%" }, 1, i => _densityIndex = i);
            _humans = NumberField(dialog, "인간 수", "0", 3);
            _zombies = NumberField(dialog, "좀비 수", "0", 4);
            Choice(dialog, "물자량", 5, new[] { "적음 · 50%", "기본 · 100%", "많음 · 150%" }, 1, i => _supplyIndex = i);
            _status = Label(dialog, "인간·좀비 각각 0~10000 / 시드는 정수\n기존 맵은 생성 성공 시 교체됩니다",
                new Vector2(24, -410), new Vector2(418, 54), 14);
            MakeButton(dialog, "닫기", new Vector2(24, -472), new Vector2(108, 38), CloseDialog);
            _generate = MakeButton(dialog, "생성", new Vector2(314, -472), new Vector2(128, 38), BeginGeneration);
            _dialog.SetActive(false);
        }

        // 생성창 표시와 지도 입력 차단
        private void OpenDialog()
        {
            _dialog.SetActive(true);
            _view.IsInputBlocked = true;
        }

        // 생성 중 닫기 방지와 지도 입력 복귀
        private void CloseDialog()
        {
            if (_generation != null) { return; }
            _dialog.SetActive(false);
            _view.IsInputBlocked = false;
        }

        // 입력값 검증 후 중복 없는 생성 시작
        private void BeginGeneration()
        {
            if (_generation != null) { return; }
            if (!int.TryParse(_seed.text, out int seed) ||
                !int.TryParse(_humans.text, out int humans) ||
                !int.TryParse(_zombies.text, out int zombies))
            {
                _status.text = "시드와 인구에 올바른 정수를 입력해 주세요";
                return;
            }
            var settings = new CitySettings
            {
                Size = _sizes[_sizeIndex], Seed = seed, Density = _densities[_densityIndex],
                Humans = humans, Zombies = zombies, Supplies = _supplies[_supplyIndex]
            };
            try { settings.Validate(); }
            catch (ArgumentException exception) { _status.text = exception.Message; return; }
            _generate.interactable = false;
            _status.text = "도시를 생성하고 있습니다";
            _generation = StartCoroutine(Generate(settings));
        }

        // 진행 안내를 표시한 다음 지도 생성과 교체
        private IEnumerator Generate(CitySettings settings)
        {
            yield return null;
            try
            {
                var map = CityGenerator.Generate(settings);
                _view.Show(map);
                _summary.text = $"섬 전체 지역  |  {map.Size} × {map.Size}  |  건물 {map.Buildings.Count}채\n인간 {map.Humans.Count} · 좀비 {map.Zombies.Count}  |  드래그 이동 · 휠 확대";
                _welcome.gameObject.SetActive(false);
                _status.text = "생성 완료 · 시드 " + settings.Seed;
            }
            catch (Exception exception)
            {
                _status.text = "생성 실패: " + exception.Message;
                if (!(exception is ArgumentException)) { Debug.LogException(exception, this); }
            }
            _generate.interactable = true;
            _generation = null;
            if (_status.text.StartsWith("생성 완료", StringComparison.Ordinal)) { CloseDialog(); }
        }

        // 비활성화 시 생성 핸들과 입력 차단 상태 정리
        private void OnDisable()
        {
            if (_generation != null) { StopCoroutine(_generation); _generation = null; }
            if (_generate != null) { _generate.interactable = true; }
            if (_view != null) { _view.IsInputBlocked = false; }
        }
    }
}
