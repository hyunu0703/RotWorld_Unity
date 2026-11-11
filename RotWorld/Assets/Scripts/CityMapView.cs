using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RotWorld
{
    /// <summary>
    /// 도시 데이터를 정사각형 픽셀 원본으로 표시하고 정수 줌과 이동 제공
    /// </summary>
    public sealed class CityMapView : MonoBehaviour
    {
        // 지도 표시 해상도와 정수 줌 범위
        public const int PixelsPerTile = 8;
        private const int MaxZoom = 6;
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
        private bool isDragging;
        public bool IsInputBlocked { get; set; }

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

            DrawPeople(pixels, width, map.Humans, false);
            DrawPeople(pixels, width, map.Zombies, true);

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
            mapTexture = nextTexture;
            mapSprite = nextSprite;
            overviewTexture = nextOverview;
            overviewSprite = nextOverviewSprite;
            this.overviewPixelsPerUnit = overviewPixelsPerUnit;
            mapData = map;
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
        private static void DrawPeople(Color32[] pixels, int stride, System.Collections.Generic.List<Vector2Int> people, bool zombie)
        {
            // 개체 타일 위치를 픽셀 좌표로 환산해 유형별 실루엣 표시
            foreach (var person in people)
            {
                int x = person.x * PixelsPerTile + 2;
                int y = person.y * PixelsPerTile + 1;
                Fill(pixels, stride, x, y, 1, 2, Hex(0x293539));
                Fill(pixels, stride, x + 2, y, 1, 2, Hex(0x293539));
                Fill(pixels, stride, x, y + 2, 3, 3, zombie ? Hex(0x7a6d52) : Hex(0x5696af));
                Fill(pixels, stride, x + 1, y + 5, 2, 2, zombie ? Hex(0x91a171) : Hex(0xd8b08b));
            }
        }


        // 정수 배율 변경
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


        // 화면 높이에서 정수 픽셀 배율 도출
        private void UpdateCamera()
        {
            // 연결된 카메라 확인 후 줌 단계별 픽셀 배율 결정
            if (viewCamera == null)
            {
                return;
            }

            float pixelsPerUnit = PixelsPerTile * Mathf.Max(1, zoom);

            // 전체 보기 단계에서는 간소화 지도와 화면 여백 기준 배율 사용
            if (mapData != null && zoom == 0)
            {
                int availableHeight = Mathf.Max(1, viewCamera.pixelHeight - ToolbarHeightAllowance);
                int overviewScale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(viewCamera.pixelWidth, availableHeight) / (overviewTexture.width * OverviewMarginScale)));
                pixelsPerUnit = overviewScale * overviewPixelsPerUnit;
                spriteRenderer.sprite = overviewSprite;
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

            if (mouse.leftButton.wasPressedThisFrame)
            {
                isDragging = !overUi;
            }

            if (mouse.leftButton.wasReleasedThisFrame)
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
                ChangeZoom(mouse.scroll.ReadValue().y > 0 ? 1 : -1);
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
        }
    }
}
