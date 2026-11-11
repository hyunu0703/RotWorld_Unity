using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RotWorld
{
    /// <summary>
    /// 기본 Unity UI를 통한 도시 생성 설정과 결과 연결
    /// </summary>
    public sealed class CityPrototype : MonoBehaviour
    {
        // 기존 씬 직렬화 이름을 유지하는 폰트와 카메라 참조
        [FormerlySerializedAs("_koreanFont")]
        [SerializeField] private TMP_FontAsset koreanFont;
        [FormerlySerializedAs("_englishFont")]
        [SerializeField] private TMP_FontAsset englishFont;
        [FormerlySerializedAs("_worldCamera")]
        [SerializeField] private Camera worldCamera;

        // 설정 입력 행 배치와 정수 입력 길이
        private const int FirstSettingsRowY = -112;
        private const int SettingsRowHeight = 48;
        private const int IntegerCharacterLimit = 11;

        // 생성 UI의 배경과 컨트롤 색상
        private static readonly Color PanelColor = new Color32(35, 43, 52, 255);
        private static readonly Color ControlColor = new Color32(57, 69, 80, 255);

        // 지도 표시와 생성창 상태 안내
        private CityMapView mapView;
        private GameObject generationDialog;
        private TMP_Text statusLabel;
        private TMP_Text summaryLabel;
        private TMP_Text welcomeLabel;

        // 생성 설정 입력과 중복 실행 제어
        private TMP_InputField seedInput;
        private TMP_InputField humansInput;
        private TMP_InputField zombiesInput;
        private Button generateButton;
        private Coroutine generationCoroutine;

        // 생성 설정별 현재 선택 위치
        private int sizeIndex = 1;
        private int densityIndex = 1;
        private int supplyIndex = 1;

        // 맵 크기와 개발 밀도 및 물자량 선택값
        private readonly int[] sizes = { 128, 256, 512, 1024 };
        private readonly int[] densities = { 40, 65, 85 };
        private readonly int[] supplies = { 50, 100, 150 };

        // 필수 씬 참조 검증과 시제품 구성
        private void Start()
        {
            // 생성 UI 구성에 필요한 카메라와 폰트 누락 검사
            if (koreanFont == null || englishFont == null || worldCamera == null)
            {
                Debug.LogError("도시 시제품의 카메라 또는 neodgm SDF 폰트 참조가 없습니다", this);
                enabled = false;
                return;
            }

            var mapObject = new GameObject("Generated City");
            mapObject.transform.SetParent(transform, false);
            mapView = mapObject.AddComponent<CityMapView>();
            mapView.Initialize(worldCamera);
            BuildInterface();
        }


        // 직사각형 기본 UI 오브젝트 생성
        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            // 부모와 앵커 기준으로 UI 사각형 배치
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
            // UI 사각형에 단색 배경 컴포넌트 연결
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }


        // 언어별 SDF 폰트를 적용한 텍스트 생성
        private TMP_Text Label(Transform parent, string text, Vector2 position, Vector2 size, float fontSize = 18, bool english = false)
        {
            // 언어에 대응하는 폰트와 텍스트 표시 속성 적용
            var rect = Rect(text, parent, new Vector2(0, 1), new Vector2(0, 1), position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = english ? englishFont : koreanFont;
            label.text = text;
            label.fontSize = fontSize;
            label.color = new Color32(235, 234, 216, 255);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }


        // 기본 색 전환과 텍스트를 가진 버튼 생성
        private Button MakeButton(Transform parent, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            // 버튼 배경과 상호작용 색상 및 클릭 동작 연결
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
        private TMP_InputField NumberField(Transform parent, string title, string initial, int row, bool signed = false)
        {
            // 설정 행 위치에 숫자 입력 영역과 텍스트 뷰포트 구성
            float y = FirstSettingsRowY - row * SettingsRowHeight;
            Label(parent, title, new Vector2(24, y), new Vector2(160, 36));
            var rect = Rect(title + " 입력", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(198, y), new Vector2(244, 36));
            Background(rect, ControlColor);
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect("Viewport", rect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -2), new Vector2(224, 32));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label(viewport, initial, Vector2.zero, new Vector2(224, 32), 18, true);
            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)text;
            input.contentType = signed ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Custom;

            if (!signed)
            {
                input.characterValidation = TMP_InputField.CharacterValidation.Digit;
            }

            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = IntegerCharacterLimit;
            input.text = initial;
            return input;
        }


        // 열거형 설정 행과 선택 버튼 생성
        private void Choice(Transform parent, string title, int row, string[] names, int initial, Action<int> changed)
        {
            // 현재 선택 위치를 보관한 이전 및 다음 버튼 구성
            float y = FirstSettingsRowY - row * SettingsRowHeight;
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
            // 화면 기준 캔버스와 입력 이벤트 시스템 구성
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

            // 지도 도구 모음과 요약 표시 영역 구성
            var bar = Rect("Bottom Toolbar", canvasRect, new Vector2(0, 0), Vector2.zero, Vector2.zero, new Vector2(0, 64));
            bar.anchorMax = new Vector2(1, 0);
            Background(bar, PanelColor);
            summaryLabel = Label(bar, "새로운 도시를 만들어 주세요", new Vector2(22, -12), new Vector2(720, 40), 16);
            var tools = Rect("Map Tools", bar, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -10), new Vector2(438, 44));
            MakeButton(tools, "축소", Vector2.zero, new Vector2(62, 44), () => mapView.ChangeZoom(-1));
            MakeButton(tools, "확대", new Vector2(68, 0), new Vector2(62, 44), () => mapView.ChangeZoom(1));
            MakeButton(tools, "중앙", new Vector2(136, 0), new Vector2(62, 44), () => mapView.Recenter());
            MakeButton(tools, "맵 생성하기", new Vector2(214, 0), new Vector2(224, 44), OpenDialog);
            welcomeLabel = Label(canvasRect, "도시의 시작\n\n우측 하단의 맵 생성하기를 눌러 주세요", new Vector2(0, 0), new Vector2(600, 120), 24);
            var welcomeRect = welcomeLabel.rectTransform;
            welcomeRect.anchorMin = welcomeRect.anchorMax = new Vector2(.5f, .5f);
            welcomeRect.pivot = new Vector2(.5f, .5f);
            welcomeLabel.alignment = TextAlignmentOptions.Center;
            // 생성 설정 입력과 상태 안내 및 실행 버튼 구성
            var dialog = Rect("Map Generation", canvasRect, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 78), new Vector2(466, 526));
            generationDialog = dialog.gameObject;
            Background(dialog, PanelColor);
            Label(dialog, "새로운 세계", new Vector2(24, -16), new Vector2(350, 40), 25);
            Label(dialog, "도심·시가지·마을·강변·산업이 있는 섬", new Vector2(24, -62), new Vector2(418, 32), 16);
            Choice(dialog, "맵 크기", 0, new[] { "소형 128", "중형 256", "대형 512", "초대형 1024" }, 1, i => sizeIndex = i);
            seedInput = NumberField(dialog, "시드", "12345", 1, true);
            Choice(dialog, "개발 밀도", 2, new[] { "낮음 · 40%", "기본 · 65%", "높음 · 85%" }, 1, i => densityIndex = i);
            humansInput = NumberField(dialog, "인간 수", "0", 3);
            zombiesInput = NumberField(dialog, "좀비 수", "0", 4);
            Choice(dialog, "물자량", 5, new[] { "적음 · 50%", "기본 · 100%", "많음 · 150%" }, 1, i => supplyIndex = i);
            statusLabel = Label(dialog, "인간·좀비 각각 0~10000 / 시드는 정수\n기존 맵은 생성 성공 시 교체됩니다", new Vector2(24, -410), new Vector2(418, 54), 14);
            MakeButton(dialog, "닫기", new Vector2(24, -472), new Vector2(108, 38), CloseDialog);
            generateButton = MakeButton(dialog, "생성", new Vector2(314, -472), new Vector2(128, 38), BeginGeneration);
            generationDialog.SetActive(false);
        }


        // 생성창 표시와 지도 입력 차단
        private void OpenDialog()
        {
            // 설정창 활성화와 지도 입력 차단 상태 동기화
            generationDialog.SetActive(true);
            mapView.IsInputBlocked = true;
        }


        // 생성 중 닫기 방지와 지도 입력 복귀
        private void CloseDialog()
        {
            // 진행 중인 생성의 설정창 닫기 차단
            if (generationCoroutine != null)
            {
                return;
            }

            generationDialog.SetActive(false);
            mapView.IsInputBlocked = false;
        }


        // 입력값 검증 후 중복 없는 생성 시작
        private void BeginGeneration()
        {
            // 중복 생성 차단 후 설정창의 정수 입력 검사
            if (generationCoroutine != null)
            {
                return;
            }

            if (!int.TryParse(seedInput.text, out int seed) ||
                !int.TryParse(humansInput.text, out int humans) ||
                !int.TryParse(zombiesInput.text, out int zombies))
            {
                statusLabel.text = "시드와 인구에 올바른 정수를 입력해 주세요";
                return;
            }

            // 검증된 정수 입력과 선택 인덱스에서 생성 설정 구성
            var settings = new CitySettings
            {
                Size = sizes[sizeIndex],
                Seed = seed,
                Density = densities[densityIndex],
                Humans = humans,
                Zombies = zombies,
                Supplies = supplies[supplyIndex]
            };
            try
            {
                settings.Validate();
            }
            catch (ArgumentException exception)
            {
                statusLabel.text = exception.Message;
                return;
            }

            // 설정 검증 성공 후 버튼 잠금과 생성 핸들 보관
            generateButton.interactable = false;
            statusLabel.text = "도시를 생성하고 있습니다";
            generationCoroutine = StartCoroutine(Generate(settings));
        }


        // 진행 안내를 표시한 다음 지도 생성과 교체
        private IEnumerator Generate(CitySettings settings)
        {
            // 진행 안내를 한 프레임 표시한 뒤 생성 결과를 지도 뷰에 전달
            yield return null;
            try
            {
                var map = CityGenerator.Generate(settings);
                mapView.Show(map);
                summaryLabel.text = $"섬 전체 지역  |  {map.Size} × {map.Size}  |  건물 {map.Buildings.Count}채\n인간 {map.Humans.Count} · 좀비 {map.Zombies.Count}  |  드래그 이동 · 휠 확대";
                welcomeLabel.gameObject.SetActive(false);
                statusLabel.text = "생성 완료 · 시드 " + settings.Seed;
            }
            catch (Exception exception)
            {
                // 사용자에게 실패 원인 안내 및 예상하지 못한 예외 기록
                statusLabel.text = "생성 실패: " + exception.Message;

                if (!(exception is ArgumentException))
                {
                    Debug.LogException(exception, this);
                }
            }

            // 성공과 실패 모두 생성 잠금 해제 후 성공한 설정창 닫기
            generateButton.interactable = true;
            generationCoroutine = null;

            if (statusLabel.text.StartsWith("생성 완료", StringComparison.Ordinal))
            {
                CloseDialog();
            }
        }


        // 비활성화 시 생성 핸들과 입력 차단 상태 정리
        private void OnDisable()
        {
            // 실행 중 생성 중단 후 버튼과 지도 입력 상태 복구
            if (generationCoroutine != null)
            {
                StopCoroutine(generationCoroutine);
                generationCoroutine = null;
            }

            if (generateButton != null)
            {
                generateButton.interactable = true;
            }

            if (mapView != null)
            {
                mapView.IsInputBlocked = false;
            }
        }
    }
}
