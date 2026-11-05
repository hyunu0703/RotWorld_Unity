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
        public const int PixelsPerTile = 8;
        private const int MaxZoom = 6;
        private Texture2D _texture;
        private Sprite _sprite;
        private Texture2D _overviewTexture;
        private Sprite _overviewSprite;
        private float _overviewPixelsPerUnit;
        private SpriteRenderer _renderer;
        private Camera _camera;
        private CityMap _map;
        private Vector2 _center;
        private int _zoom = 1;
        private bool _isDragging;
        public bool IsInputBlocked { get; set; }
        public CityMap Map => _map;

        // 기존 씬 카메라와 표시 컴포넌트 연결
        public void Initialize(Camera worldCamera)
        {
            _camera = worldCamera;
            _camera.orthographic = true;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.backgroundColor = Hex(0x264d68);
            _renderer = gameObject.AddComponent<SpriteRenderer>();
        }

        // 원본 픽셀 배열 생성 후 기존 표시 자원 교체
        public void Show(CityMap map)
        {
            int width = map.Size * PixelsPerTile;
            var pixels = new Color32[width * width];
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte tile = map.Tiles[(y / PixelsPerTile) * map.Size + x / PixelsPerTile];
                    uint hash = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ map.Seed));
                    Color32 color = tile == CityGenerator.Grass
                        ? ((hash % 19 == 0) ? Hex(0x6f8051) : Hex(0x61764a))
                        : tile == CityGenerator.Ocean ? Hex(0x264d68)
                        : tile == CityGenerator.Shallows ? Hex(0x397f91)
                        : tile == CityGenerator.River ? Hex(0x397f91)
                        : tile == CityGenerator.Bridge ? Hex(0x9e947c)
                        : tile == CityGenerator.Sand ? ((hash % 23 == 0) ? Hex(0xc4b57c) : Hex(0xd2c38e))
                        : tile == CityGenerator.Road ? Hex(0x454c51)
                        : tile == CityGenerator.Path ? Hex(0xb5ab8d) : Hex(0x91978c);
                    if (tile == CityGenerator.Pavement && (x % 8 == 0 || y % 8 == 0)) { color = Hex(0x7f877e); }
                    CityRegion region = map.Regions[(y / PixelsPerTile) * map.Size + x / PixelsPerTile];
                    if (tile == CityGenerator.Grass)
                    {
                        if (region == CityRegion.Downtown) { color = Hex(0x85877b); }
                        else if (region == CityRegion.Industrial) { color = Hex(0x8e8876); }
                        else if (region == CityRegion.Riverside) { color = Hex(0x70865b); }
                        else if (region == CityRegion.Natural) { color = Hex(0x526d45); }
                    }
                    if (tile == CityGenerator.Bridge && (x % 8 == 0 || y % 8 == 0)) { color = Hex(0x766e61); }
                    pixels[y * width + x] = color;
                }
            }
            foreach (var building in map.Buildings) { DrawBuilding(pixels, width, building); }
            DrawPeople(pixels, width, map.Humans, false);
            DrawPeople(pixels, width, map.Zombies, true);
            var nextTexture = new Texture2D(width, width, TextureFormat.RGBA32, false);
            nextTexture.name = "City pixels";
            nextTexture.filterMode = FilterMode.Point;
            nextTexture.wrapMode = TextureWrapMode.Clamp;
            nextTexture.SetPixels32(pixels);
            nextTexture.Apply(false, true);
            var nextSprite = Sprite.Create(nextTexture, new Rect(0, 0, width, width),
                Vector2.zero, PixelsPerTile, 0, SpriteMeshType.FullRect);
            // 전체 보기에서는 최대 256픽셀 지도로 명시적 간소화
            int overviewSize = Mathf.Min(256, map.Size);
            int sampleStep = width / overviewSize;
            var overviewPixels = new Color32[overviewSize * overviewSize];
            for (int y = 0; y < overviewSize; y++)
            {
                for (int x = 0; x < overviewSize; x++)
                {
                    overviewPixels[y * overviewSize + x] = pixels[(y * sampleStep + sampleStep / 2) * width +
                        x * sampleStep + sampleStep / 2];
                }
            }
            var nextOverview = new Texture2D(overviewSize, overviewSize, TextureFormat.RGBA32, false);
            nextOverview.name = "Island overview pixels";
            nextOverview.filterMode = FilterMode.Point;
            nextOverview.wrapMode = TextureWrapMode.Clamp;
            nextOverview.SetPixels32(overviewPixels);
            nextOverview.Apply(false, true);
            float overviewPixelsPerUnit = overviewSize / (float)map.Size;
            var nextOverviewSprite = Sprite.Create(nextOverview, new Rect(0, 0, overviewSize, overviewSize),
                Vector2.zero, overviewPixelsPerUnit, 0, SpriteMeshType.FullRect);
            ReleaseGraphics();
            _texture = nextTexture;
            _sprite = nextSprite;
            _overviewTexture = nextOverview;
            _overviewSprite = nextOverviewSprite;
            _overviewPixelsPerUnit = overviewPixelsPerUnit;
            _map = map;
            _center = new Vector2(map.Size / 2f, map.Size / 2f);
            _zoom = 0;
            UpdateCamera();
        }

        // 팔레트 정수값을 불투명 색으로 변환
        private static Color32 Hex(int rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }

        // 경계 안의 픽셀 사각형 채우기
        private static void Fill(Color32[] pixels, int stride, int x, int y, int width, int height, Color32 color)
        {
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
                Fill(pixels, stride, x + 3, y + 7, w - 6, 2,
                    building.Kind == 1 ? Hex(0xb69a56) : Hex(0x666975));
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
            int doorX = building.Door.x * PixelsPerTile;
            int doorY = building.Door.y * PixelsPerTile;
            Fill(pixels, stride, doorX + 2, doorY + 2, 4, 4, Hex(0xd1c6a0));
        }

        // 인도 위 작은 인물과 좀비 실루엣 표시
        private static void DrawPeople(Color32[] pixels, int stride,
            System.Collections.Generic.List<Vector2Int> people, bool zombie)
        {
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
            _zoom = Mathf.Clamp(_zoom + delta, 0, MaxZoom);
            UpdateCamera();
        }

        // 지도 중심으로 복귀
        public void Recenter()
        {
            if (_map == null) { return; }
            _center = new Vector2(_map.Size / 2f, _map.Size / 2f);
            UpdateCamera();
        }

        // 화면 높이에서 정수 픽셀 배율 도출
        private void UpdateCamera()
        {
            if (_camera == null) { return; }
            float pixelsPerUnit = PixelsPerTile * Mathf.Max(1, _zoom);
            if (_map != null && _zoom == 0)
            {
                int availableHeight = Mathf.Max(1, _camera.pixelHeight - 100);
                int overviewScale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(_camera.pixelWidth, availableHeight) /
                    (_overviewTexture.width * 1.12f)));
                pixelsPerUnit = overviewScale * _overviewPixelsPerUnit;
                _renderer.sprite = _overviewSprite;
            }
            else if (_renderer != null) { _renderer.sprite = _sprite; }
            _camera.orthographicSize = Mathf.Max(1, _camera.pixelHeight) / (2f * pixelsPerUnit);
            float snapPixelsPerUnit = _map != null && _zoom == 0 ? _overviewPixelsPerUnit : PixelsPerTile;
            _camera.transform.position = new Vector3(
                Mathf.Round(_center.x * snapPixelsPerUnit) / snapPixelsPerUnit,
                Mathf.Round(_center.y * snapPixelsPerUnit) / snapPixelsPerUnit, -10);
        }

        // UI 외부 드래그 이동과 휠 줌 처리
        private void Update()
        {
            if (_map == null || _camera == null) { return; }
            UpdateCamera();
            var mouse = Mouse.current;
            if (mouse == null || IsInputBlocked) { _isDragging = false; return; }
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (mouse.leftButton.wasPressedThisFrame) { _isDragging = !overUi; }
            if (mouse.leftButton.wasReleasedThisFrame) { _isDragging = false; }
            if (_isDragging)
            {
                _center -= mouse.delta.ReadValue() * (2f * _camera.orthographicSize / Mathf.Max(1, _camera.pixelHeight));
                _center.x = Mathf.Clamp(_center.x, 0, _map.Size);
                _center.y = Mathf.Clamp(_center.y, 0, _map.Size);
            }
            if (!overUi && mouse.scroll.ReadValue().y != 0)
            {
                ChangeZoom(mouse.scroll.ReadValue().y > 0 ? 1 : -1);
            }
        }

        // 런타임 그래픽 자원 해제
        private void ReleaseGraphics()
        {
            if (_renderer != null) { _renderer.sprite = null; }
            if (_sprite != null) { Destroy(_sprite); }
            if (_texture != null) { Destroy(_texture); }
            if (_overviewSprite != null) { Destroy(_overviewSprite); }
            if (_overviewTexture != null) { Destroy(_overviewTexture); }
        }

        // 씬 종료 시 소유 자원 정리
        private void OnDestroy()
        {
            ReleaseGraphics();
        }
    }
}
