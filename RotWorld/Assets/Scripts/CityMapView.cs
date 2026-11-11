using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RotWorld
{
    /// <summary>
    /// 도시 데이터를 정사각형 픽셀 원본으로 표시하고 단계별 줌과 이동 제공
    /// </summary>
    public sealed class CityMapView : MonoBehaviour
    {
        // 지도 표시 해상도와 전체 보기 전환 단계
        public const int PixelsPerTile = 8;
        private const int UnitPixelScale = 2;
        private const int OverviewTransitionSteps = 4;
        private const int MaxZoom = 6 + OverviewTransitionSteps - 1;
        private const int MaximumOverviewSize = 256;
        private const int ToolbarHeightAllowance = 100;
        private const float OverviewMarginScale = 1.12f;
        private const int CameraDepth = -10;

        // 타일 좌표 해시와 지형 질감 빈도
        private const int HorizontalHashMultiplier = 73856093;
        private const int VerticalHashMultiplier = 19349663;
        private const int GrassVariationInterval = 19;
        private const int SandVariationInterval = 23;

        // 원본 지도 그래픽 자원 소유
        private Texture2D mapTexture;
        private Sprite mapSprite;

        // 전체 보기 그래픽 자원과 픽셀 배율
        private Texture2D overviewTexture;
        private Sprite overviewSprite;
        private float overviewPixelsPerUnit;

        // 지도 표시 컴포넌트와 현재 데이터
        private SpriteRenderer spriteRenderer;
        private Camera viewCamera;
        private CityMap mapData;
        public CityMap Map => mapData;

        // 카메라 중심과 사용자 입력 상태
        private Vector2 viewCenter;
        private int zoom = 1;
        public float ZoomProgress => zoom / (float)MaxZoom;
        private bool isDragging;
        public bool IsInputBlocked { get; set; }

        // 유형별 공용 소환 아이콘과 소환 개체 보관
        private Sprite humanIcon;
        private Sprite zombieIcon;
        private Sprite[] humanWalkFrames;
        private Sprite[] zombieWalkFrames;
        private Sprite[] humanSummonFrames;
        private Sprite[] zombieSummonFrames;
        private Sprite[] conversionFrames;
        private UnitDeathVisuals zombieDeathVisuals;
        private Transform summonedUnits;

        // 기존 씬 카메라와 표시 컴포넌트 연결
        public void Initialize(Camera worldCamera)
        {
            // 전달받은 카메라의 지도 표시 설정과 렌더러 구성
            viewCamera = worldCamera;
            viewCamera.orthographic = true;
            viewCamera.allowHDR = false;
            viewCamera.allowMSAA = false;
            viewCamera.backgroundColor = Hex(0x264d68);
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }


        // 원본 픽셀 배열 생성 후 기존 표시 자원 교체
        public void Show(CityMap map)
        {
            // 지도 타일과 시드에서 원본 픽셀 색상 생성
            int width = map.Size * PixelsPerTile;
            var pixels = new Color32[width * width];

            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte tile = map.Tiles[(y / PixelsPerTile) * map.Size + x / PixelsPerTile];
                    uint hash = unchecked((uint)(x * HorizontalHashMultiplier ^ y * VerticalHashMultiplier ^ map.Seed));
                    Color32 color = tile == CityGenerator.Grass
                        ? ((hash % GrassVariationInterval == 0) ? Hex(0x6f8051) : Hex(0x61764a))
                        : tile == CityGenerator.Ocean ? Hex(0x264d68)
                        : tile == CityGenerator.Shallows ? Hex(0x397f91)
                        : tile == CityGenerator.River ? Hex(0x397f91)
                        : tile == CityGenerator.Bridge ? Hex(0x9e947c)
                        : tile == CityGenerator.Sand
                            ? ((hash % SandVariationInterval == 0) ? Hex(0xc4b57c) : Hex(0xd2c38e))
                        : tile == CityGenerator.Road ? Hex(0x454c51)
                        : tile == CityGenerator.Path ? Hex(0xb5ab8d) : Hex(0x91978c);

                    if (tile == CityGenerator.Pavement && (x % PixelsPerTile == 0 || y % PixelsPerTile == 0))
                    {
                        color = Hex(0x7f877e);
                    }

                    CityRegion region = map.Regions[(y / PixelsPerTile) * map.Size + x / PixelsPerTile];

                    if (tile == CityGenerator.Grass)
                    {
                        if (region == CityRegion.Downtown)
                        {
                            color = Hex(0x85877b);
                        }
                        else if (region == CityRegion.Industrial)
                        {
                            color = Hex(0x8e8876);
                        }
                        else if (region == CityRegion.Riverside)
                        {
                            color = Hex(0x70865b);
                        }
                        else if (region == CityRegion.Natural)
                        {
                            color = Hex(0x526d45);
                        }
                    }

                    if (tile == CityGenerator.Bridge && (x % PixelsPerTile == 0 || y % PixelsPerTile == 0))
                    {
                        color = Hex(0x766e61);
                    }

                    pixels[y * width + x] = color;
                }
            }

            foreach (var building in map.Buildings)
            {
                DrawBuilding(pixels, width, building);
            }

            // 완성된 픽셀 배열을 원본 표시 자원에 업로드
            var nextTexture = new Texture2D(width, width, TextureFormat.RGBA32, false);
            nextTexture.name = "City pixels";
            nextTexture.filterMode = FilterMode.Point;
            nextTexture.wrapMode = TextureWrapMode.Clamp;
            nextTexture.SetPixels32(pixels);
            nextTexture.Apply(false, true);
            var nextSprite = Sprite.Create(nextTexture, new Rect(0, 0, width, width), Vector2.zero, PixelsPerTile, 0, SpriteMeshType.FullRect);
            // 전체 보기에서는 최대 256픽셀 지도로 명시적 간소화
            int overviewSize = Mathf.Min(MaximumOverviewSize, map.Size);
            int sampleStep = width / overviewSize;
            var overviewPixels = new Color32[overviewSize * overviewSize];

            for (int y = 0; y < overviewSize; y++)
            {
                for (int x = 0; x < overviewSize; x++)
                {
                    overviewPixels[y * overviewSize + x] = pixels[(y * sampleStep + sampleStep / 2) * width + x * sampleStep + sampleStep / 2];
                }
            }

            var nextOverview = new Texture2D(overviewSize, overviewSize, TextureFormat.RGBA32, false);
            nextOverview.name = "Island overview pixels";
            nextOverview.filterMode = FilterMode.Point;
            nextOverview.wrapMode = TextureWrapMode.Clamp;
            nextOverview.SetPixels32(overviewPixels);
            nextOverview.Apply(false, true);
            float overviewPixelsPerUnit = overviewSize / (float)map.Size;
            var nextOverviewSprite = Sprite.Create(nextOverview, new Rect(0, 0, overviewSize, overviewSize), Vector2.zero, overviewPixelsPerUnit, 0, SpriteMeshType.FullRect);
            // 새 표시 자원 준비 후 기존 자원 교체
            ReleaseGraphics();

            // 새 지도 교체 시 이전 지도에 소환한 표시 객체 정리
            if (summonedUnits != null)
            {
                Destroy(summonedUnits.gameObject);
            }

            summonedUnits = new GameObject("Summoned Units").transform;
            summonedUnits.SetParent(transform, false);
            mapTexture = nextTexture;
            mapSprite = nextSprite;
            overviewTexture = nextOverview;
            overviewSprite = nextOverviewSprite;
            this.overviewPixelsPerUnit = overviewPixelsPerUnit;
            mapData = map;

            // 초기 인구도 소환 유닛과 같은 정밀도와 공용 아이콘으로 표시
            for (int i = 0; i < map.Humans.Count; i++)
            {
                CreateUnitVisual(map.Humans[i], false, i);
            }

            for (int i = 0; i < map.Zombies.Count; i++)
            {
                CreateUnitVisual(map.Zombies[i], true, i);
            }

            viewCenter = new Vector2(map.Size / 2f, map.Size / 2f);
            zoom = 0;
            UpdateCamera();
        }


        // 팔레트 정수값을 불투명 색으로 변환
        private static Color32 Hex(int rgb)
        {
            // RGB 정수의 채널 분리와 불투명 알파 적용
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }


        // 경계 안의 픽셀 사각형 채우기
        private static void Fill(Color32[] pixels, int stride, int x, int y, int width, int height, Color32 color)
        {
            // 픽셀 배열 경계 안으로 제한한 사각형 영역 채우기
            for (int py = Mathf.Max(0, y); py < Mathf.Min(stride, y + height); py++)
            {
                for (int px = Mathf.Max(0, x); px < Mathf.Min(stride, x + width); px++)
                {
                    pixels[py * stride + px] = color;
                }
            }
        }


        // 용도별 지붕과 벽 및 출입구 표시
        private static void DrawBuilding(Color32[] pixels, int stride, CityBuilding building)
        {
            // 건물 필지를 픽셀 좌표로 환산한 뒤 외형과 출입구 표시
            var r = building.Bounds;
            int x = r.x * PixelsPerTile;
            int y = r.y * PixelsPerTile;
            int w = r.width * PixelsPerTile;
            int h = r.height * PixelsPerTile;
            Color32 roof = building.Kind == 0 ? Hex(0x9d604b) : building.Kind == 1 ? Hex(0x64848b)
                : building.Kind == 3 ? Hex(0x596977) : building.Kind == 4 ? Hex(0x789b96) : Hex(0x8a8890);
            Fill(pixels, stride, x + 2, y - 2, w, h, Hex(0x424b40));
            Fill(pixels, stride, x, y, w, h, Hex(0x353e3f));
            Fill(pixels, stride, x + 1, y + 1, w - 2, 6, Hex(0xc1b494));
            Fill(pixels, stride, x + 1, y + 7, w - 2, h - 8, roof);
            Fill(pixels, stride, x + 3, y + h - 3, w - 6, 1, Hex(0xc2b194));

            for (int wx = x + 5; wx < x + w - 4; wx += 9)
            {
                Fill(pixels, stride, wx, y + 2, 3, 3, Hex(0x465c67));
            }

            if (building.Kind == 0)
            {
                Fill(pixels, stride, x + 2, y + h / 2, w - 4, 2, Hex(0x794c40));
                Fill(pixels, stride, x + w - 10, y + h - 10, 4, 6, Hex(0x555453));
            }
            else
            {
                Fill(pixels, stride, x + w / 2, y + h / 2, 7, 5, Hex(0xb4b8ad));
                Fill(pixels, stride, x + 3, y + 7, w - 6, 2, building.Kind == 1 ? Hex(0xb69a56) : Hex(0x666975));
            }

            if (building.Kind == 3)
            {
                for (int floorY = y + 9; floorY < y + h - 3; floorY += 6)
                {
                    for (int wx = x + 4; wx < x + w - 3; wx += 6)
                    {
                        Fill(pixels, stride, wx, floorY, 2, 2, Hex(0xb8c5bc));
                    }
                }
            }

            // 생성 데이터의 실제 출입구 위치를 지붕 위에 표시
            int doorX = building.Door.x * PixelsPerTile;
            int doorY = building.Door.y * PixelsPerTile;
            Fill(pixels, stride, doorX + 2, doorY + 2, 4, 4, Hex(0xd1c6a0));
        }


        // 인도 위 작은 인물과 좀비 실루엣 표시
        private static void DrawPeople(Color32[] pixels, int stride, System.Collections.Generic.List<Vector2Int> people, bool zombie, int walkFrame = 0)
        {
            // 기존 크기를 유지한 두 배 격자에서 반 픽셀 위치 표현
            const int headSize = 2;
            const int bodyWidth = 3;
            int headOffset = (bodyWidth - headSize) * UnitPixelScale / 2;

            foreach (var person in people)
            {
                int x = (person.x * PixelsPerTile + 2) * UnitPixelScale;
                int y = person.y * PixelsPerTile * UnitPixelScale;
                int leftLift = walkFrame == 1 ? UnitPixelScale : 0;
                int rightLift = walkFrame == 2 ? UnitPixelScale : 0;
                Fill(pixels, stride, x, y + leftLift, UnitPixelScale, 2 * UnitPixelScale - leftLift, Hex(0x293539));
                Fill(pixels, stride, x + 2 * UnitPixelScale, y + rightLift, UnitPixelScale, 2 * UnitPixelScale - rightLift, Hex(0x293539));
                Fill(pixels, stride, x, y + 2 * UnitPixelScale, bodyWidth * UnitPixelScale, 3 * UnitPixelScale,
                    zombie ? Hex(0x7a6d52) : Hex(0x5696af));
                // 불투명 단색 머리의 좌우 어깨 여백을 동일하게 배치
                Fill(pixels, stride, x + headOffset, y + 5 * UnitPixelScale, headSize * UnitPixelScale, headSize * UnitPixelScale,
                    zombie ? Hex(0x91a171) : Hex(0xd8b08b));
            }
        }


        // 전체 보기 중간 단계와 상세 배율 변경
        public void ChangeZoom(int delta)
        {
            // 지원 배율 범위로 보정한 뒤 카메라 갱신
            zoom = Mathf.Clamp(zoom + delta, 0, MaxZoom);
            UpdateCamera();
        }


        // 지도 중심으로 복귀
        public void Recenter()
        {
            // 표시할 지도 유무 확인 후 지도 중심 좌표 복원
            if (mapData == null)
            {
                return;
            }

            viewCenter = new Vector2(mapData.Size / 2f, mapData.Size / 2f);
            UpdateCamera();
        }


        // 화면 높이에서 전체 보기와 상세 보기 배율 도출
        private void UpdateCamera()
        {
            // 연결된 카메라 확인 후 줌 단계별 픽셀 배율 결정
            if (viewCamera == null)
            {
                return;
            }

            int detailZoom = Mathf.Max(1, zoom - OverviewTransitionSteps + 1);
            float pixelsPerUnit = PixelsPerTile * detailZoom;

            // 전체 보기와 상세 원본 사이에 동일 비율의 중간 확대 단계 적용
            if (mapData != null && zoom < OverviewTransitionSteps)
            {
                int availableHeight = Mathf.Max(1, viewCamera.pixelHeight - ToolbarHeightAllowance);
                int overviewScale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(viewCamera.pixelWidth, availableHeight) / (overviewTexture.width * OverviewMarginScale)));
                float overviewDisplayScale = overviewScale * overviewPixelsPerUnit;
                float transitionProgress = zoom / (float)OverviewTransitionSteps;
                pixelsPerUnit = overviewDisplayScale * Mathf.Pow(PixelsPerTile / overviewDisplayScale, transitionProgress);
                spriteRenderer.sprite = zoom == 0 ? overviewSprite : mapSprite;
            }
            else if (spriteRenderer != null)
            {
                spriteRenderer.sprite = mapSprite;
            }

            // 픽셀 격자에 맞춘 카메라 크기와 중심 좌표 적용
            viewCamera.orthographicSize = Mathf.Max(1, viewCamera.pixelHeight) / (2f * pixelsPerUnit);
            float snapPixelsPerUnit = mapData != null && zoom == 0 ? overviewPixelsPerUnit : PixelsPerTile;
            viewCamera.transform.position = new Vector3(
                Mathf.Round(viewCenter.x * snapPixelsPerUnit) / snapPixelsPerUnit,
                Mathf.Round(viewCenter.y * snapPixelsPerUnit) / snapPixelsPerUnit, CameraDepth);
        }


        // UI 외부 드래그 이동과 휠 줌 처리
        private void Update()
        {
            // 지도와 카메라 준비 상태 확인 후 프레임 입력 처리
            if (mapData == null || viewCamera == null)
            {
                return;
            }

            UpdateCamera();
            var mouse = Mouse.current;

            if (mouse == null || IsInputBlocked)
            {
                isDragging = false;
                return;
            }

            // UI 위 입력으로 지도 드래그 시작과 휠 확대 방지
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (mouse.rightButton.wasPressedThisFrame)
            {
                isDragging = !overUi;
            }

            if (mouse.rightButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }

            if (isDragging)
            {
                viewCenter -= mouse.delta.ReadValue() * (2f * viewCamera.orthographicSize / Mathf.Max(1, viewCamera.pixelHeight));
                viewCenter.x = Mathf.Clamp(viewCenter.x, 0, mapData.Size);
                viewCenter.y = Mathf.Clamp(viewCenter.y, 0, mapData.Size);
            }

            if (!overUi && mouse.scroll.ReadValue().y != 0)
            {
                // 드래그 결과 반영 후 커서 아래의 줌 이전 지도 좌표 저장
                UpdateCamera();
                Vector2 cursorPosition = mouse.position.ReadValue();
                Vector2 anchorBeforeZoom = viewCamera.ScreenToWorldPoint(cursorPosition);
                int previousZoom = zoom;
                ChangeZoom(mouse.scroll.ReadValue().y > 0 ? 1 : -1);

                // 실제 배율 변경 시 커서 아래 좌표 차이만큼 카메라 중심 보정
                if (zoom != previousZoom)
                {
                    Vector2 anchorAfterZoom = viewCamera.ScreenToWorldPoint(cursorPosition);
                    viewCenter = (Vector2)viewCamera.transform.position + anchorBeforeZoom - anchorAfterZoom;
                    UpdateCamera();
                }
            }
        }


        // 기존 지도 인물 표현을 재사용한 유형별 공용 소환 아이콘 조회
        public Sprite GetUnitIcon(bool zombie)
        {
            // 최초 요청 시에만 투명 배경의 한 타일 아이콘 생성
            var icon = zombie ? zombieIcon : humanIcon;

            if (icon != null)
            {
                return icon;
            }

            int iconSize = PixelsPerTile * UnitPixelScale;
            var pixels = new Color32[iconSize * iconSize];
            DrawPeople(pixels, iconSize, new System.Collections.Generic.List<Vector2Int> { Vector2Int.zero }, zombie);
            var texture = new Texture2D(iconSize, iconSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            icon = Sprite.Create(texture, new Rect(0, 0, iconSize, iconSize), Vector2.zero, iconSize);

            if (zombie)
            {
                zombieIcon = icon;
            }
            else
            {
                humanIcon = icon;
            }

            return icon;
        }


        // 화면 클릭 좌표를 지도 타일로 변환해 데이터와 유닛 표시 추가
        public bool TrySummon(Vector2 screenPosition, bool zombie)
        {
            // 지도 미준비와 설정창 입력 차단 및 카메라 외부 클릭 거부
            if (mapData == null || viewCamera == null || IsInputBlocked || !viewCamera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            Vector2 localPosition = transform.InverseTransformPoint(viewCamera.ScreenToWorldPoint(screenPosition));
            var tile = new Vector2Int(Mathf.FloorToInt(localPosition.x), Mathf.FloorToInt(localPosition.y));

            if (!UnitPathfinder.CanOccupy(mapData, tile) || mapData.OccupiedTiles.Contains(tile))
            {
                return false;
            }

            // 전체 지도 재생성 없이 클릭한 타일에 공용 아이콘 표시와 인구 데이터 반영
            var population = zombie ? mapData.Zombies : mapData.Humans;
            population.Add(tile);
            CreateUnitVisual(tile, zombie, population.Count - 1, true);
            return true;
        }


        // 초기 인구와 소환 인구가 공유하는 독립 유닛 표시 생성
        private void CreateUnitVisual(Vector2Int tile, bool zombie, int index, bool animateSummon = false)
        {
            // 지도 텍스처와 별도로 배치해 머리의 반 픽셀 정밀도 유지
            var unit = new GameObject(zombie ? "Summoned Zombie" : "Summoned Human");
            unit.transform.SetParent(summonedUnits, false);
            unit.transform.localPosition = new Vector3(tile.x, tile.y, 0);
            var visual = new GameObject("Unit Visual");
            visual.transform.SetParent(unit.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = GetUnitIcon(zombie);
            renderer.sortingOrder = spriteRenderer.sortingOrder + 1;
            var animationView = unit.AddComponent<UnitWalkAnimation>();
            animationView.Initialize(renderer, GetUnitWalkFrames(zombie));

            if (animateSummon)
            {
                animationView.PlaySummon(GetUnitWalkFrames(zombie, true));
            }
            unit.AddComponent<UnitWander>().Initialize(mapData, zombie, index, GetUnitWalkFrames(true), GetUnitConversionFrames(), GetZombieDeathVisuals());
        }


        // 유형별 원본 아이콘과 두 다리 포즈를 공유하는 걷기 프레임 조회
        private Sprite[] GetUnitWalkFrames(bool zombie, bool white = false)
        {
            // 이미 생성한 공용 프레임 재사용으로 유닛별 텍스처 복제 방지
            var frames = white ? (zombie ? zombieSummonFrames : humanSummonFrames) : (zombie ? zombieWalkFrames : humanWalkFrames);

            if (frames != null)
            {
                return frames;
            }

            const int FrameCount = 3;
            int iconSize = PixelsPerTile * UnitPixelScale;
            frames = new Sprite[FrameCount];
            if (!white)
            {
                frames[0] = GetUnitIcon(zombie);
            }

            for (int frame = white ? 0 : 1; frame < FrameCount; frame++)
            {
                var pixels = new Color32[iconSize * iconSize];
                DrawPeople(pixels, iconSize, new System.Collections.Generic.List<Vector2Int> { Vector2Int.zero }, zombie, frame);

                // 투명도를 유지한 흰색 걷기 프레임을 모든 소환 개체가 공유
                if (white)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color32(255, 255, 255, pixels[i].a);
                    }
                }
                var texture = new Texture2D(iconSize, iconSize, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                frames[frame] = Sprite.Create(texture, new Rect(0, 0, iconSize, iconSize), Vector2.zero, iconSize);
            }

            if (white)
            {
                if (zombie)
                {
                    zombieSummonFrames = frames;
                }
                else
                {
                    humanSummonFrames = frames;
                }
            }
            else if (zombie)
            {
                zombieWalkFrames = frames;
            }
            else
            {
                humanWalkFrames = frames;
            }

            return frames;
        }


        // 인간에서 좀비로 서서히 변하며 흰색으로 점멸하는 공용 프레임 생성
        private Sprite[] GetUnitConversionFrames()
        {
            // 모든 개체에서 동일한 픽셀 변환 자원 공유
            if (conversionFrames != null)
            {
                return conversionFrames;
            }

            const int FrameCount = 101;
            const float ConversionDuration = 5f;
            const float FirstFlashTime = 1f;
            const float FlashInterval = 1f;
            const float FlashDuration = .2f;
            const int FlashCount = 4;
            const float FlashStrength = 1f;
            int size = PixelsPerTile * UnitPixelScale;
            var human = new Color32[size * size];
            var zombie = new Color32[size * size];
            var origin = new System.Collections.Generic.List<Vector2Int> { Vector2Int.zero };
            DrawPeople(human, size, origin, false);
            DrawPeople(zombie, size, origin, true);
            conversionFrames = new Sprite[FrameCount];
            conversionFrames[0] = GetUnitIcon(false);
            conversionFrames[FrameCount - 1] = GetUnitIcon(true);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                float progress = frame / (float)(FrameCount - 1);
                // 일 초부터 사 초까지만 네 번 점멸하고 오 초에는 변환 완료
                float elapsed = progress * ConversionDuration;
                int flashIndex = Mathf.FloorToInt((elapsed - FirstFlashTime) / FlashInterval);
                float flashTime = elapsed - FirstFlashTime - flashIndex * FlashInterval;
                bool isFlashing = flashIndex >= 0 && flashIndex < FlashCount && flashTime >= 0f && flashTime < FlashDuration;
                float pulse = isFlashing ? Mathf.Pow(Mathf.Cos(flashTime / FlashDuration * Mathf.PI * .5f), 2f) * FlashStrength : 0f;
                var pixels = new Color32[human.Length];

                for (int i = 0; i < pixels.Length; i++)
                {
                    // 투명 배경을 유지하고 불투명 몸체만 좀비 색상과 흰색으로 보간
                    Color color = Color.Lerp(human[i], zombie[i], Mathf.SmoothStep(0f, 1f, progress));

                    if (color.a > 0f)
                    {
                        color = Color.Lerp(color, Color.white, pulse);
                    }

                    pixels[i] = color;
                }

                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                conversionFrames[frame] = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.zero, size);
            }

            return conversionFrames;
        }


        // 쓰러진 좀비의 흰색 전환 프레임과 먼지 픽셀 공용 자원 생성
        private UnitDeathVisuals GetZombieDeathVisuals()
        {
            // 모든 좀비와 변환 개체가 동일 사망 자원 공유
            if (zombieDeathVisuals != null)
            {
                return zombieDeathVisuals;
            }

            const int FrameCount = 51;
            int size = PixelsPerTile * UnitPixelScale;
            var zombie = new Color32[size * size];
            DrawPeople(zombie, size, new System.Collections.Generic.List<Vector2Int> { Vector2Int.zero }, true);
            var fadeFrames = new Sprite[FrameCount];

            for (int frame = 0; frame < FrameCount; frame++)
            {
                // 투명 배경을 유지하고 불투명 몸체만 좀비 색상에서 흰색으로 보간
                float progress = frame / (float)(FrameCount - 1);
                var pixels = new Color32[zombie.Length];

                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = zombie[i].a > 0 ? (Color32)Color.Lerp(zombie[i], Color.white, progress) : zombie[i];
                }

                fadeFrames[frame] = CreatePointSprite(pixels, size, Vector2.zero, size);
            }

            // 화면 도트 한 칸 크기의 공용 흰색 먼지 픽셀 생성
            var pixelColor = new[] { new Color32(255, 255, 255, 255) };
            Sprite pixelSprite = CreatePointSprite(pixelColor, 1, new Vector2(.5f, .5f), PixelsPerTile);

            // 확대 격자 한 칸을 원본 도트 한 개로 묶어 몸체 픽셀 중심 좌표 수집
            var consumed = new bool[zombie.Length];
            var dustPoints = new System.Collections.Generic.List<Vector2>();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (zombie[y * size + x].a == 0 || consumed[y * size + x])
                    {
                        continue;
                    }

                    for (int blockY = y; blockY < Mathf.Min(size, y + UnitPixelScale); blockY++)
                    {
                        for (int blockX = x; blockX < Mathf.Min(size, x + UnitPixelScale); blockX++)
                        {
                            consumed[blockY * size + blockX] = true;
                        }
                    }

                    dustPoints.Add(new Vector2(x + UnitPixelScale * .5f, y + UnitPixelScale * .5f) / size);
                }
            }

            zombieDeathVisuals = new UnitDeathVisuals(fadeFrames, pixelSprite, dustPoints.ToArray(), 1f / PixelsPerTile);
            return zombieDeathVisuals;
        }


        // 점 필터 텍스처로 원본 도트를 보존하는 스프라이트 생성
        private static Sprite CreatePointSprite(Color32[] pixels, int size, Vector2 pivot, float pixelsPerUnit)
        {
            // 읽기 해제된 정사각형 텍스처에 픽셀 배열 적용
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), pivot, pixelsPerUnit);
        }


        // 좀비 사망 전용 프레임과 먼지 픽셀 자원 해제
        private void ReleaseZombieDeathVisuals()
        {
            // 아직 생성하지 않은 사망 자원 정리 생략
            if (zombieDeathVisuals == null)
            {
                return;
            }

            foreach (var frame in zombieDeathVisuals.FadeFrames)
            {
                Destroy(frame.texture);
                Destroy(frame);
            }

            if (zombieDeathVisuals.PixelSprite != null)
            {
                Destroy(zombieDeathVisuals.PixelSprite.texture);
                Destroy(zombieDeathVisuals.PixelSprite);
            }

            zombieDeathVisuals = null;
        }


        // 변환 전용 프레임 자원 해제
        private void ReleaseConversionFrames()
        {
            // 시작과 완료 프레임을 포함한 변환 전용 자원 전체 해제
            if (conversionFrames == null)
            {
                return;
            }

            for (int i = 0; i < conversionFrames.Length; i++)
            {
                Destroy(conversionFrames[i].texture);
                Destroy(conversionFrames[i]);
            }

            conversionFrames = null;
        }

        // 원본 아이콘을 제외한 걷기 전용 자원 해제
        private void ReleaseWalkFrames(Sprite[] frames, bool includeFirst = false)
        {
            // 아직 생성하지 않은 유형의 프레임 정리 생략
            if (frames == null)
            {
                return;
            }

            for (int i = includeFirst ? 0 : 1; i < frames.Length; i++)
            {
                Destroy(frames[i].texture);
                Destroy(frames[i]);
            }
        }

        // 런타임 그래픽 자원 해제
        private void ReleaseGraphics()
        {
            // 렌더러 참조 해제 후 소유 스프라이트와 텍스처 파괴
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = null;
            }

            if (mapSprite != null)
            {
                Destroy(mapSprite);
            }

            if (mapTexture != null)
            {
                Destroy(mapTexture);
            }

            if (overviewSprite != null)
            {
                Destroy(overviewSprite);
            }

            if (overviewTexture != null)
            {
                Destroy(overviewTexture);
            }
        }


        // 씬 종료 시 소유 자원 정리
        private void OnDestroy()
        {
            // 씬 종료 시 생성한 그래픽 자원의 수명 종료
            ReleaseGraphics();

            // 유형별 걷기 전용 프레임과 공용 아이콘의 소유 자원 해제
            ReleaseConversionFrames();
            ReleaseZombieDeathVisuals();
            ReleaseWalkFrames(humanWalkFrames);
            ReleaseWalkFrames(zombieWalkFrames);
            ReleaseWalkFrames(humanSummonFrames, true);
            ReleaseWalkFrames(zombieSummonFrames, true);

            if (humanIcon != null)
            {
                Destroy(humanIcon.texture);
                Destroy(humanIcon);
            }

            if (zombieIcon != null)
            {
                Destroy(zombieIcon.texture);
                Destroy(zombieIcon);
            }
        }
    }
}
