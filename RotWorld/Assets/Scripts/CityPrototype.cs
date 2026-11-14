using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

        // 하단 도구 모음과 소환 패널 높이
        private const float BottomToolbarHeight = 64f;
        private const float UnitPanelHeight = 100f;

        // 생성 UI의 배경과 컨트롤 색상
        private static readonly Color PanelColor = new Color32(35, 43, 52, 255);
        private static readonly Color ControlColor = new Color32(57, 69, 80, 255);

        // 지도 표시와 생성창 상태 안내
        private CityMapView mapView;
        private GameObject generationDialog;
        private TMP_Text statusLabel;
        private TMP_Text summaryLabel;
        private CityMap summaryMap;
        private TMP_Text welcomeLabel;

        // Q키로 함께 표시를 전환할 하단 도구 모음과 자식 패널
        private GameObject bottomToolbar;
        private GameObject unitSummonPanel;

        // 지도 생성 후 표시할 일차와 24시간 형식 시계
        private const float DateBarHeight = 52f;
        private const float DateLabelInset = 22f;
        private const float DateLabelWidth = 240f;
        private const float DateLabelFontSize = 24f;
        private readonly GameClock gameClock = new GameClock();
        private GameObject dateBar;
        private TMP_Text dayLabel;
        private TMP_Text clockLabel;
        private long displayedGameSeconds = -1;
        private long displayedDay = -1;
        private readonly char[] clockCharacters = new char[8];

        // 하단 시간 제어의 배속 선택과 벡터 아이콘 보관
        private static readonly float[] SimulationSpeeds = { 1f, 0f, 2f, 4f, 8f };
        private static readonly string[] SimulationSpeedNames = { "실행", "일시정지", "2배속", "4배속", "8배속" };
        private static readonly SimulationTimeIcon.Shape[] SimulationIconShapes =
        {
            SimulationTimeIcon.Shape.Play, SimulationTimeIcon.Shape.Pause, SimulationTimeIcon.Shape.FastForward,
            SimulationTimeIcon.Shape.TripleFastForward, SimulationTimeIcon.Shape.Lightning
        };
        private const float TimeButtonWidth = 62f;
        private const float TimeButtonHeight = 44f;
        private const float TimeButtonGap = 6f;
        private const float TimeControlsRightInset = 520f;
        private const float TimeIconWidth = 20f;
        private const float TimeIconHeight = 12f;
        private const float LightningIconWidth = 12f;
        private const float LightningIconHeight = 20f;
        private const float PauseIconWidth = 24f;
        private const float PauseIconHeight = 16f;
        private const float TimeSpeedFontSize = 15f;
        private static readonly Color SelectedTimeColor = new Color32(255, 217, 92, 255);
        private readonly Outline[] timeSelectionOutlines = new Outline[5];
        private readonly SimulationTimeIcon[] timeIcons = new SimulationTimeIcon[5];
        private readonly TMP_Text[] timeSpeedLabels = new TMP_Text[5];
        private bool hasTimeControls;

        // 단일 소환 선택과 버튼 외곽선 및 커서 표시
        private enum SummonSelection { None, Human, Zombie }
        private SummonSelection summonSelection;
        private Outline humanOutline;
        private Outline zombieOutline;
        private Image summonCursor;
        private RectTransform interfaceRect;
        private PointerEventData summonPointer;
        private readonly List<RaycastResult> summonRaycasts = new List<RaycastResult>();
        private const float SummonIconSize = 40f;
        private const float SummonButtonSize = 72f;
        private const float SummonButtonPadding = 6f;
        private const float SummonButtonGap = 16f;
        private const float SummonNameFontSize = 14f;
        private const float CursorClickDuration = .2f;
        private const float CursorClickScale = .6f;
        private float cursorClickRemaining;
        private const float MinimumSummonCursorScale = .5f;
        private const float SummonOutlineWidth = 3f;
        private static readonly Color SummonOutlineColor = new Color32(255, 217, 92, 255);

        // 선택한 인간의 생존 수치와 화면 위치 표시
        private HumanNeedsPanel humanNeedsPanel;

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


        // 게임 시계 진행과 Q키 입력에 따른 하단 도구 모음 표시 전환
        private void Update()
        {
            // 지도 생성 이후 공통 배속이 적용된 시간을 시계와 인간 생존 수치에 전달
            if (mapView != null && mapView.Map != null)
            {
                double previousGameSeconds = gameClock.TotalElapsedSeconds;
                gameClock.Advance(Time.deltaTime);
                double elapsedGameSeconds = gameClock.TotalElapsedSeconds - previousGameSeconds;

                if (elapsedGameSeconds > 0d)
                {
                    // 같은 생성 시점의 인간은 동일한 덧셈을 한 번만 수행
                    mapView.Map.NeedsSchedule.Advance(elapsedGameSeconds);
                }

                RefreshClockDisplay();
            }

            // UI 구성 전 또는 키보드 부재 시 입력 처리 생략
            var keyboard = Keyboard.current;

            if (bottomToolbar == null || keyboard == null || !keyboard.qKey.wasPressedThisFrame)
            {
                return;
            }

            // 부모 활성 상태 반전으로 하단 도구 모음과 소환 패널 동시 전환
            bottomToolbar.SetActive(!bottomToolbar.activeSelf);

            if (!bottomToolbar.activeSelf)
            {
                SetSummonSelection(SummonSelection.None);
            }
        }


        // UI 이벤트 처리 이후 인간 정보 표시와 커서 위치 갱신 및 지도 클릭 소환
        private void LateUpdate()
        {
            // 소환 선택 중 클릭 충돌 방지와 선택된 인간의 이동 추적
            if (humanNeedsPanel != null)
            {
                humanNeedsPanel.Refresh(summonSelection == SummonSelection.None);
            }

            // 비활성 선택과 입력 차단 상태에서 커서 숨김
            if (summonCursor == null)
            {
                return;
            }

            var mouse = Mouse.current;
            bool canSummon = summonSelection != SummonSelection.None && mouse != null &&
                unitSummonPanel.activeInHierarchy && !mapView.IsInputBlocked && Application.isFocused;
            summonCursor.gameObject.SetActive(canSummon);

            if (!canSummon)
            {
                return;
            }

            // 화면 좌표를 캔버스 좌표로 변환하고 커서 아이콘에 전달
            Vector2 screenPosition = mouse.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(interfaceRect, screenPosition, null, out Vector2 localPosition);
            summonCursor.rectTransform.anchoredPosition = localPosition;

            // 클릭 축소를 현재 줌 크기와 독립적으로 복원
            cursorClickRemaining = Mathf.Max(0f, cursorClickRemaining - Time.unscaledDeltaTime);
            float clickProgress = 1f - cursorClickRemaining / CursorClickDuration;
            summonCursor.rectTransform.localScale = Vector3.one * Mathf.Lerp(CursorClickScale, 1f, Mathf.SmoothStep(0f, 1f, clickProgress));

            // 지도 줌 단계에 따라 커서만 기존 크기의 절반부터 원래 크기까지 보간
            float cursorSize = SummonIconSize * Mathf.Lerp(MinimumSummonCursorScale, 1f, mapView.ZoomProgress);
            summonCursor.rectTransform.sizeDelta = new Vector2(cursorSize, cursorSize);

            if (!mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            // 현재 좌표의 UI를 직접 검사해 버튼 클릭의 지도 전달 방지
            summonPointer.position = screenPosition;
            summonRaycasts.Clear();
            EventSystem.current.RaycastAll(summonPointer, summonRaycasts);

            if (summonRaycasts.Count != 0)
            {
                return;
            }

            // 지도 클릭마다 축소를 다시 시작해 연속 클릭에도 즉시 반응
            cursorClickRemaining = CursorClickDuration;
            summonCursor.rectTransform.localScale = Vector3.one * CursorClickScale;

            if (mapView.TrySummon(screenPosition, summonSelection == SummonSelection.Zombie))
            {
                RefreshSummary();
            }
        }


        // 같은 버튼 재선택 해제와 다른 유형 선택의 단일 상태 전환
        private void ToggleSummonSelection(SummonSelection selection)
        {
            // 기존 선택과 요청 선택 비교 후 버튼 및 커서 동기화
            SetSummonSelection(summonSelection == selection ? SummonSelection.None : selection);
        }


        // 소환 선택에 따른 외곽선과 커서 동기화
        private void SetSummonSelection(SummonSelection selection)
        {
            // 단일 선택값에서 두 버튼의 활성 표시 도출
            summonSelection = selection;
            cursorClickRemaining = 0f;
            summonCursor.rectTransform.localScale = Vector3.one;
            humanOutline.enabled = selection == SummonSelection.Human;
            zombieOutline.enabled = selection == SummonSelection.Zombie;
            summonCursor.gameObject.SetActive(selection != SummonSelection.None);

            if (selection != SummonSelection.None)
            {
                summonCursor.sprite = mapView.GetUnitIcon(selection == SummonSelection.Zombie);
            }
        }


        // 기존 인구 데이터에서 지도 요약 표시 갱신
        private void RefreshSummary()
        {
            // 생성과 소환이 공유하는 데이터의 현재 인구 표시
            var map = mapView.Map;

            if (summaryMap != map)
            {
                if (summaryMap != null)
                {
                    summaryMap.PopulationChanged -= RefreshSummary;
                }

                summaryMap = map;
                summaryMap.PopulationChanged += RefreshSummary;
            }

            summaryLabel.text = $"섬 전체 지역  |  {map.Size} × {map.Size}  |  건물 {map.Buildings.Count}채\n인간 {map.HumanCount} · 좀비 {map.ZombieCount}  |  드래그 이동 · 휠 확대";
        }


        // UI 종료 시 현재 지도 인구 변경 구독 해제
        private void OnDestroy()
        {
            ReleaseTimeControls();

            // 지도 수명과 독립된 UI 콜백 정리
            if (summaryMap != null)
            {
                summaryMap.PopulationChanged -= RefreshSummary;
            }
        }


        // 소환 버튼의 유형별 픽셀 아이콘과 선택 외곽선 구성
        private Outline MakeSummonButton(Transform parent, string title, Vector2 position, SummonSelection selection)
        {
            // 기존 버튼 생성 기능을 통한 정사각형 유닛 칸 구성
            var buttonSize = new Vector2(SummonButtonSize, SummonButtonSize);
            var button = MakeButton(parent, title, position, buttonSize, () => ToggleSummonSelection(selection));

            // 칸 안쪽 우측 하단에 유닛 이름 배치
            var text = button.GetComponentInChildren<TMP_Text>();
            text.fontSize = SummonNameFontSize;
            text.rectTransform.anchoredPosition = new Vector2(SummonButtonPadding, -SummonButtonPadding);
            text.rectTransform.sizeDelta = buttonSize - Vector2.one * (SummonButtonPadding * 2f);
            text.alignment = TextAlignmentOptions.BottomRight;

            // 이름 위쪽에 기존 픽셀 아이콘 배치 및 선택 외곽선 연결
            var iconRect = Rect("Unit Icon", button.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -SummonButtonPadding),
                new Vector2(SummonIconSize, SummonIconSize));
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = mapView.GetUnitIcon(selection == SummonSelection.Zombie);
            icon.raycastTarget = false;
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = SummonOutlineColor;
            outline.effectDistance = new Vector2(SummonOutlineWidth, -SummonOutlineWidth);
            outline.enabled = false;
            return outline;
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


        // 화면 상단 양쪽에 일차와 현재 시각 표시 영역 구성
        private void BuildDateBar(RectTransform canvasRect)
        {
            // 화면 너비에 맞춘 상단 배경과 맵 생성 전 숨김 처리
            var bar = Rect("Date Bar", canvasRect, Vector2.up, Vector2.up, Vector2.zero,
                new Vector2(0f, DateBarHeight));
            bar.anchorMax = Vector2.one;
            Background(bar, PanelColor);
            dateBar = bar.gameObject;

            // 왼쪽 일차와 오른쪽 시각에 각각 한국어 및 숫자 폰트 적용
            dayLabel = Label(bar, "", new Vector2(DateLabelInset, 0f),
                new Vector2(DateLabelWidth, DateBarHeight), DateLabelFontSize);
            clockLabel = Label(bar, "", new Vector2(-DateLabelInset, 0f),
                new Vector2(DateLabelWidth, DateBarHeight), DateLabelFontSize, true);
            clockLabel.rectTransform.anchorMin = Vector2.one;
            clockLabel.rectTransform.anchorMax = Vector2.one;
            clockLabel.rectTransform.pivot = Vector2.one;
            clockLabel.alignment = TextAlignmentOptions.MidlineRight;
            dateBar.SetActive(false);
        }


        // 게임 시각이 바뀐 경우에만 상단 일차와 시각 갱신
        private void RefreshClockDisplay()
        {
            // 정지 및 같은 게임 초 동안 중복 문자열 생성 방지
            if (displayedGameSeconds == gameClock.ElapsedSeconds)
            {
                return;
            }

            // 자정에 증가한 일차와 하루 안의 시각을 UI에 함께 전달
            displayedGameSeconds = gameClock.ElapsedSeconds;
            if (displayedDay != gameClock.CurrentDay)
            {
                displayedDay = gameClock.CurrentDay;
                dayLabel.text = $"{displayedDay}일차";
            }

            // 매 프레임 문자열 할당 없이 동일한 두 자리 시간 형식 기록
            clockCharacters[0] = (char)('0' + gameClock.Hour / 10);
            clockCharacters[1] = (char)('0' + gameClock.Hour % 10);
            clockCharacters[2] = ':';
            clockCharacters[3] = (char)('0' + gameClock.Minute / 10);
            clockCharacters[4] = (char)('0' + gameClock.Minute % 10);
            clockCharacters[5] = ':';
            clockCharacters[6] = (char)('0' + gameClock.Second / 10);
            clockCharacters[7] = (char)('0' + gameClock.Second % 10);
            clockLabel.SetCharArray(clockCharacters);
        }


        // 하단 중앙의 실행 및 정지와 배속 아이콘 버튼 구성
        private void BuildTimeControls(RectTransform bar)
        {
            // 지도 도구 왼쪽의 빈 영역에 다섯 버튼을 고정 간격으로 배치
            float width = SimulationSpeeds.Length * TimeButtonWidth + (SimulationSpeeds.Length - 1) * TimeButtonGap;
            var controls = Rect("Simulation Time Controls", bar, Vector2.one, Vector2.one,
                new Vector2(-TimeControlsRightInset, -10f), new Vector2(width, TimeButtonHeight));

            for (int i = 0; i < SimulationSpeeds.Length; i++)
            {
                int selection = i;
                var size = new Vector2(TimeButtonWidth, TimeButtonHeight);
                var button = MakeButton(controls, "", new Vector2(i * (TimeButtonWidth + TimeButtonGap), 0f),
                    size, () => SelectSimulationSpeed(selection));
                button.name = SimulationSpeedNames[i];
                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = SelectedTimeColor;
                outline.effectDistance = new Vector2(2f, -2f);
                timeSelectionOutlines[i] = outline;

                // 삼각형과 정지 막대 및 세로형 번개의 개별 비율과 배속 숫자 유지
                bool isFastForward = i >= 2;
                var iconSize = i == 1 ? new Vector2(PauseIconWidth, PauseIconHeight) : new Vector2(TimeIconWidth, TimeIconHeight);

                if (SimulationIconShapes[i] == SimulationTimeIcon.Shape.Lightning)
                {
                    iconSize = new Vector2(LightningIconWidth, LightningIconHeight);
                }

                var iconRect = Rect("Time Icon", button.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(isFastForward ? -12f : 0f, 0f), iconSize);
                var icon = iconRect.gameObject.AddComponent<SimulationTimeIcon>();
                icon.Initialize(SimulationIconShapes[i]);
                icon.raycastTarget = false;
                timeIcons[i] = icon;

                if (isFastForward)
                {
                    var label = Label(button.transform, SimulationSpeeds[i] + "x", new Vector2(32f, 0f),
                        new Vector2(28f, TimeButtonHeight), TimeSpeedFontSize, true);
                    label.alignment = TextAlignmentOptions.Center;
                    timeSpeedLabels[i] = label;
                }
            }

            // 최초 UI 구성 시 기본 실행 배속과 선택 표시 동기화
            hasTimeControls = true;
            SelectSimulationSpeed(0);
        }


        // 공통 시간 배율과 선택된 시간 버튼 표시 동기화
        private void SelectSimulationSpeed(int selection)
        {
            // UI에서 선택한 배율을 이동과 전투 및 각종 연출에 공통 적용
            Time.timeScale = SimulationSpeeds[selection];

            // 1배속 실행 버튼 선택 시 사각형 표시 및 다른 상태에서 삼각형 복원
            timeIcons[0].SetShape(selection == 0 ? SimulationTimeIcon.Shape.Stop : SimulationTimeIcon.Shape.Play);

            for (int i = 0; i < SimulationSpeeds.Length; i++)
            {
                bool isSelected = i == selection;
                timeSelectionOutlines[i].enabled = isSelected;
                timeIcons[i].color = isSelected ? SelectedTimeColor : Color.white;

                if (timeSpeedLabels[i] != null)
                {
                    timeSpeedLabels[i].color = isSelected ? SelectedTimeColor : Color.white;
                }
            }
        }


        // UI 파괴 시 공통 시간 복원
        private void ReleaseTimeControls()
        {
            // Q키로 숨길 때 배율 유지 및 소유 UI 파괴 시에만 복원
            if (hasTimeControls)
            {
                Time.timeScale = 1f;
            }
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

            interfaceRect = canvasRect;
            summonPointer = new PointerEventData(EventSystem.current);
            BuildDateBar(canvasRect);

            // 지도 도구 모음과 요약 표시 영역 구성
            var bar = Rect("Bottom Toolbar", canvasRect, new Vector2(0, 0), Vector2.zero, Vector2.zero, new Vector2(0, BottomToolbarHeight));
            bottomToolbar = bar.gameObject;
            bar.anchorMax = new Vector2(1, 0);
            Background(bar, PanelColor);

            // 기존 도구 모음 바로 위에 높이 100의 소환 패널 배치 및 맵 생성 전 숨김
            var unitPanel = Rect("Unit Summon Panel", bar, new Vector2(0, 1), Vector2.zero, Vector2.zero,
                new Vector2(0, UnitPanelHeight));
            unitPanel.anchorMax = Vector2.one;
            Background(unitPanel, PanelColor);
            unitSummonPanel = unitPanel.gameObject;
            unitSummonPanel.SetActive(false);
            var firstUnitPosition = new Vector2(20, -14);
            humanOutline = MakeSummonButton(unitPanel, "인간", firstUnitPosition, SummonSelection.Human);
            zombieOutline = MakeSummonButton(unitPanel, "좀비",
                firstUnitPosition + new Vector2(SummonButtonSize + SummonButtonGap, 0), SummonSelection.Zombie);

            summaryLabel = Label(bar, "새로운 도시를 만들어 주세요", new Vector2(22, -12), new Vector2(390, 40), 16);
            BuildTimeControls(bar);
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
            BuildHumanNeedsPanel(canvasRect);

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

            // 모든 UI보다 위에 표시하며 클릭을 가로채지 않는 커서 아이콘 구성
            var cursorRect = Rect("Summon Cursor", canvasRect, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(SummonIconSize, SummonIconSize));
            summonCursor = cursorRect.gameObject.AddComponent<Image>();
            summonCursor.raycastTarget = false;
            summonCursor.gameObject.SetActive(false);
        }


        // 기존 폰트와 색상을 사용하는 인간 정보 패널 구성
        private void BuildHumanNeedsPanel(RectTransform canvasRect)
        {
            // 지도 위와 인간 아래에 렌더링할 선택 표시 전용 월드 캔버스 구성
            const float PanelWidth = 220f;
            const float PanelHeight = 116f;
            var markerCanvasRect = Rect("Unit Selection Canvas", transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, Vector2.one);
            var markerCanvas = markerCanvasRect.gameObject.AddComponent<Canvas>();
            markerCanvas.renderMode = RenderMode.WorldSpace;
            markerCanvas.worldCamera = worldCamera;
            markerCanvas.overrideSorting = true;
            mapView.SetSelectionCanvasSorting(markerCanvas);
            var selectionMarker = Rect("Human Selection", markerCanvasRect, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(16f, 8f));
            var marker = selectionMarker.gameObject.AddComponent<SelectionEllipseGraphic>();
            marker.color = new Color32(110, 235, 140, 255);
            marker.raycastTarget = false;
            selectionMarker.gameObject.SetActive(false);

            // 생존 정보 패널은 기존 화면 캔버스에서 표시
            var panel = Rect("Human Needs", canvasRect, new Vector2(.5f, .5f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            Background(panel, PanelColor);
            Label(panel, "인간", new Vector2(16f, -10f), new Vector2(188f, 26f), 20f);
            var hungerLabel = Label(panel, "허기", new Vector2(16f, -44f), new Vector2(188f, 26f));
            var thirstLabel = Label(panel, "갈증", new Vector2(16f, -76f), new Vector2(188f, 26f));

            // 클릭과 위치 갱신은 독립 표시 객체에 위임
            humanNeedsPanel = new HumanNeedsPanel(mapView, worldCamera, canvasRect, panel, hungerLabel, thirstLabel, selectionMarker);
            panel.gameObject.SetActive(false);
        }


        // 생성창 표시와 지도 입력 차단
        private void OpenDialog()
        {
            // 설정창 활성화와 지도 입력 차단 상태 동기화
            SetSummonSelection(SummonSelection.None);
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
                // 지도 교체 성공 후 첫날 자정부터 새 시계 표시 시작
                gameClock.Reset();
                displayedGameSeconds = -1;
                RefreshClockDisplay();
                dateBar.SetActive(true);

                // 새 도시의 시뮬레이션을 기본 실행 배속으로 시작
                SelectSimulationSpeed(0);
                // 지도 표시 성공 후 소환 패널 표시 허용
                unitSummonPanel.SetActive(true);
                RefreshSummary();
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
            // UI 구성 완료 상태에서 소환 모드와 커서 해제
            if (summonCursor != null && mapView != null)
            {
                SetSummonSelection(SummonSelection.None);
            }

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
