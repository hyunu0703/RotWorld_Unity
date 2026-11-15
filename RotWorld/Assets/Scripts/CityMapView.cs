using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RotWorld
{
    /// <summary>
    /// 도시 데이터와 구역 지형 표시를 연결하고 기존 카메라와 소환 조작 제공
    /// </summary>
    public sealed class CityMapView : MonoBehaviour
    {
        // 지도 표시 해상도와 전체 보기 전환 단계
        public const int PixelsPerTile = CityArtCatalog.PixelsPerTile;
        private const int LegacyPixelsPerTile = 8;
        private const int UnitPixelScale = 2;
        private const int OverviewTransitionSteps = 4;
        // 상세 보기는 원본 타일 픽셀의 정수배만 사용하고 기존 최대 확대율 48픽셀 유지
        private const int DetailZoomSteps = 3;
        private const int MaxZoom = DetailZoomSteps + OverviewTransitionSteps - 1;
        // 모든 맵 크기의 줌 단계별 화면 배율을 중형 맵 기준으로 통일
        private const int ReferenceMapSize = 256;
        private const int ToolbarHeightAllowance = 100;
        private const float OverviewMarginScale = 1.12f;
        private const int CameraDepth = -10;
        private const int MapSortingOrder = 0;

        // 카메라 계산 입력과 마지막 적용 상태
        private CityMap cameraMap;
        private Vector2 cameraCenter;
        private int cameraZoom = int.MinValue;
        private int cameraWidth;
        private int cameraHeight;
        private float cameraSize;
        private Vector3 cameraPosition;

        // 지형 카탈로그와 지도별 구역 자원 소유
        private CityArtCatalog terrainArt;
        private CityChunkRenderer terrainRenderer;

        // 전체 보기 그래픽 자원. 0번은 타일당 1픽셀, 이후 단계마다 절반 해상도
        private Texture2D[] overviewTextures;
        private Sprite[] overviewSprites;

        // 지도 표시 컴포넌트와 현재 데이터
        private SpriteRenderer spriteRenderer;
        private Camera viewCamera;
        private CityMap mapData;
        public CityMap Map => mapData;

        // 카메라 중심과 사용자 입력 상태
        private Vector2 viewCenter;
        private int zoom = 1;
        // 중형 맵보다 큰 맵에만 생기는 추가 축소 단계 수만큼 음수
        private int minZoom;
        // 소환 커서 크기는 중형 맵 기준 단계로만 계산해 맵 크기와 무관하게 유지
        public float ZoomProgress => Mathf.Max(0, zoom) / (float)MaxZoom;
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
        private UnitDeathVisuals humanDeathVisuals;
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
            // 축소 지도는 상세 구역과 선택 및 유닛보다 먼저 표시
            spriteRenderer.sortingOrder = MapSortingOrder - 1;
        }


        // Scene에서 전달한 지형 카탈로그 연결
        public void ConfigureArt(CityArtCatalog art)
        {
            // 필수 아트 누락을 지도 생성 전에 명시적으로 중단
            if (art == null)
            {
                throw new System.ArgumentNullException(nameof(art));
            }

            art.Prepare();
            terrainArt = art;
        }


        // 축소 지도와 지연 생성 구역을 준비한 뒤 기존 지도 교체
        public void Show(CityMap map)
        {
            // 기존 생성 데이터와 Scene 아트 연결 상태 검사
            if (map == null || terrainArt == null || viewCamera == null)
            {
                throw new System.InvalidOperationException("지도 또는 지형 카탈로그 또는 카메라 연결 누락");
            }

            // 중형 맵보다 큰 맵은 크기가 두 배 늘 때마다 절반 해상도의 추가 축소 단계 하나 생성
            int extraSteps = 0;

            while ((map.Size >> extraSteps) > ReferenceMapSize)
            {
                extraSteps++;
            }

            var nextTextures = new Texture2D[extraSteps + 1];
            var nextSprites = new Sprite[extraSteps + 1];
            CityChunkRenderer nextRenderer = null;

            try
            {
                for (int level = 0; level <= extraSteps; level++)
                {
                    nextTextures[level] = CreateOverviewTexture(map, map.Size >> level);
                    nextSprites[level] = Sprite.Create(nextTextures[level],
                        new Rect(0, 0, nextTextures[level].width, nextTextures[level].height), Vector2.zero,
                        nextTextures[level].width / (float)map.Size, 0, SpriteMeshType.FullRect);
                }

                nextRenderer = new CityChunkRenderer(map, terrainArt, transform, MapSortingOrder);
            }
            catch
            {
                // 새 자원 준비 실패 시 기존 지도 유지와 부분 생성 자원 반환
                if (nextRenderer != null)
                {
                    nextRenderer.Dispose();
                }

                DestroyOverviews(nextTextures, nextSprites);
                throw;
            }

            ReleaseGraphics();

            // 새 지도 교체 시 이전 지도에 소환한 표시 객체 정리
            if (summonedUnits != null)
            {
                Destroy(summonedUnits.gameObject);
            }

            summonedUnits = new GameObject("Summoned Units").transform;
            summonedUnits.SetParent(transform, false);
            terrainRenderer = nextRenderer;
            overviewTextures = nextTextures;
            overviewSprites = nextSprites;
            minZoom = -extraSteps;
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

            // 첫 화면은 맵 크기와 무관하게 섬 전체가 보이는 최대 축소 단계로 시작
            viewCenter = new Vector2(map.Size / 2f, map.Size / 2f);
            zoom = minZoom;
            cameraZoom = int.MinValue;
            UpdateCamera();
        }


        // 지정한 한 변 픽셀 수로 섬 전체를 직접 샘플링한 축소 지도 생성
        private Texture2D CreateOverviewTexture(CityMap map, int overviewSize)
        {
            // 텍스처 픽셀과 지도 타일의 비율로 샘플 좌표 계산
            float pixelsPerUnit = overviewSize / (float)map.Size;
            var texture = new Texture2D(overviewSize, overviewSize, TextureFormat.RGBA32, false);
            texture.name = "Island overview pixels " + overviewSize;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            try
            {
                var pixels = texture.GetRawTextureData<Color32>();

                for (int y = 0; y < overviewSize; y++)
                {
                    for (int x = 0; x < overviewSize; x++)
                    {
                        float worldX = (x + .5f) / pixelsPerUnit;
                        float worldY = (y + .5f) / pixelsPerUnit;
                        int tileX = (int)worldX;
                        int tileY = (int)worldY;
                        int index = tileY * map.Size + tileX;
                        byte tile = map.Tiles[index];
                        bool isTerrain = tile == CityGenerator.Grass || tile == CityGenerator.Sand ||
                            tile == CityGenerator.Ocean || tile == CityGenerator.Shallows || tile == CityGenerator.River;
                        bool isStreet = CityArtCatalog.IsStreet(tile);
                        pixels[y * overviewSize + x] = isTerrain
                            ? terrainArt.Sample(terrainArt.GetTileIndex(map, tileX, tileY), map.Regions[index],
                                tile == CityGenerator.Shallows || tile == CityGenerator.River, terrainArt.GetCoastMask(map, tileX, tileY),
                                (int)((worldX - tileX) * PixelsPerTile), (int)((worldY - tileY) * PixelsPerTile))
                            : isStreet ? terrainArt.SampleStreet(terrainArt.GetStreetTile(map, tileX, tileY),
                                (int)((worldX - tileX) * PixelsPerTile), (int)((worldY - tileY) * PixelsPerTile)) : Hex(0x91978c);
                    }
                }

                CityBuildingArt.PaintOverview(map, terrainArt, pixels, overviewSize, pixelsPerUnit);
                texture.Apply(false, true);
                return texture;
            }
            catch
            {
                // 생성 도중 실패한 텍스처를 호출자에게 넘기지 않고 즉시 반환
                Destroy(texture);
                throw;
            }
        }


        // 축소 지도 단계별 스프라이트와 텍스처 해제
        private void DestroyOverviews(Texture2D[] textures, Sprite[] sprites)
        {
            // 일부만 생성된 배열도 처리하도록 빈 항목 건너뜀
            if (sprites != null)
            {
                foreach (var sprite in sprites)
                {
                    if (sprite != null)
                    {
                        Destroy(sprite);
                    }
                }
            }

            if (textures != null)
            {
                foreach (var texture in textures)
                {
                    if (texture != null)
                    {
                        Destroy(texture);
                    }
                }
            }
        }


        // 팔레트 정수값을 불투명 색으로 변환
        private static Color32 Hex(int rgb)
        {
            // RGB 정수의 채널 분리와 불투명 알파 적용
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }


        // 소형 공용 아이콘의 관리 배열에 사각형 색상 기록
        private static void Fill(Color32[] pixels, int stride, int x, int y, int width, int height, Color32 color)
        {
            // 지도 버퍼와 독립적인 소형 아이콘 생성 경로 유지
            for (int py = Mathf.Max(0, y); py < Mathf.Min(stride, y + height); py++)
            {
                for (int px = Mathf.Max(0, x); px < Mathf.Min(stride, x + width); px++)
                {
                    pixels[py * stride + px] = color;
                }
            }
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
                int x = (person.x * LegacyPixelsPerTile + 2) * UnitPixelScale;
                int y = person.y * LegacyPixelsPerTile * UnitPixelScale;
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
            zoom = Mathf.Clamp(zoom + delta, minZoom, MaxZoom);
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

            // 입력과 외부 카메라 상태가 같은 경우 배율 및 표시 재계산 생략
            if (cameraMap == mapData && cameraCenter.Equals(viewCenter) && cameraZoom == zoom &&
                cameraWidth == viewCamera.pixelWidth && cameraHeight == viewCamera.pixelHeight &&
                cameraSize == viewCamera.orthographicSize && cameraPosition.Equals(viewCamera.transform.position))
            {
                return;
            }

            // 상세 보기는 원본 타일 픽셀의 정수배로 표시해 Point 샘플링의 텍셀 누락 방지
            int detailZoom = Mathf.Max(1, zoom - OverviewTransitionSteps + 1);
            float pixelsPerUnit = PixelsPerTile * detailZoom;

            // 전체 보기와 상세 원본 1배율 사이에 동일 비율의 중간 확대 단계 적용
            // 축소 배율은 실제 맵이 아닌 중형 맵 크기로 계산해 맵 크기별 해상도 차이 제거
            if (mapData != null && zoom < OverviewTransitionSteps)
            {
                int availableHeight = Mathf.Max(1, viewCamera.pixelHeight - ToolbarHeightAllowance);
                int overviewScale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(viewCamera.pixelWidth, availableHeight) / (ReferenceMapSize * OverviewMarginScale)));

                if (zoom < 0)
                {
                    // 추가 축소 단계는 절반 해상도 지도를 같은 정수 배율로 표시해 픽셀 크기 균일 유지
                    int level = Mathf.Min(-zoom, overviewSprites.Length - 1);
                    pixelsPerUnit = overviewScale * overviewSprites[level].pixelsPerUnit;
                    spriteRenderer.sprite = overviewSprites[level];
                }
                else
                {
                    float transitionProgress = zoom / (float)OverviewTransitionSteps;
                    pixelsPerUnit = overviewScale * Mathf.Pow(PixelsPerTile / (float)overviewScale, transitionProgress);
                    spriteRenderer.sprite = overviewSprites[0];
                }
            }
            else if (spriteRenderer != null && overviewSprites != null)
            {
                spriteRenderer.sprite = overviewSprites[0];
            }

            // 현재 배율의 화면 픽셀 격자에 맞춘 카메라 크기와 중심 좌표 적용
            viewCamera.orthographicSize = Mathf.Max(1, viewCamera.pixelHeight) / (2f * pixelsPerUnit);
            viewCamera.transform.position = new Vector3(
                Mathf.Round(viewCenter.x * pixelsPerUnit) / pixelsPerUnit,
                Mathf.Round(viewCenter.y * pixelsPerUnit) / pixelsPerUnit, CameraDepth);
            cameraMap = mapData;
            cameraCenter = viewCenter;
            cameraZoom = zoom;
            cameraWidth = viewCamera.pixelWidth;
            cameraHeight = viewCamera.pixelHeight;
            cameraSize = viewCamera.orthographicSize;
            cameraPosition = viewCamera.transform.position;
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


        // 입력과 카메라 변경 이후 화면 주변 지형 구역 갱신
        private void LateUpdate()
        {
            // 카메라 입력 중단 상태와 독립적인 지연 생성 및 캐시 표시 처리
            if (terrainRenderer != null)
            {
                terrainRenderer.UpdateVisible(viewCamera, zoom > 0);
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

            int iconSize = LegacyPixelsPerTile * UnitPixelScale;
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


        // 지도와 인간 사이의 선택 표시 렌더링 순서 설정
        internal void SetSelectionCanvasSorting(Canvas selectionCanvas)
        {
            // 지도 렌더러의 레이어를 공유하고 인간보다 한 단계 아래에 배치
            selectionCanvas.sortingLayerID = spriteRenderer.sortingLayerID;
            selectionCanvas.sortingOrder = MapSortingOrder + UnitWalkAnimation.LivingSortingOffset - 1;
        }


        // 화면 클릭 영역에서 가장 가까운 살아 있는 인간 조회
        internal UnitWander GetHumanAtScreenPosition(Vector2 screenPosition)
        {
            // 생성창과 카메라 외부 클릭의 선택 차단
            if (mapData == null || viewCamera == null || IsInputBlocked || !viewCamera.pixelRect.Contains(screenPosition))
            {
                return null;
            }

            // 기존 몸체 영역과 화면상 최소 클릭 여유를 조합해 작은 유닛 선택 지원
            const float MinimumClickSize = 12f;
            UnitWander closest = null;
            float closestDistance = float.PositiveInfinity;
            Vector2 localClick = transform.InverseTransformPoint(viewCamera.ScreenToWorldPoint(screenPosition));
            CityBuilding occluder = terrainRenderer != null ? terrainRenderer.GetBuildingAt(localClick) : null;

            if (occluder != null && !terrainRenderer.IsBuildingBodyVisible(occluder))
            {
                // 축소 지도 위에 표시되는 유닛의 기존 선택 우선순위 유지
                occluder = null;
            }

            foreach (var human in mapData.HumanUnits.Values)
            {
                if (human == null || !human.isActiveAndEnabled || !human.IsHumanTarget)
                {
                    continue;
                }

                // 지붕 뒤에 가려진 인물이 건물 클릭을 가로채는 상황 방지
                if (occluder != null && CityArtCatalog.GetDepthOrder(human.transform.localPosition.y) +
                    UnitWalkAnimation.LivingSortingOffset < CityArtCatalog.GetDepthOrder(occluder.Bounds.yMin))
                {
                    continue;
                }

                Rect body = UnitWalkAnimation.BodyBounds;
                Vector2 min = viewCamera.WorldToScreenPoint(human.transform.TransformPoint(body.min));
                Vector2 max = viewCamera.WorldToScreenPoint(human.transform.TransformPoint(body.max));
                Vector2 center = (min + max) * .5f;
                Vector2 size = Vector2.Max(max - min, Vector2.one * MinimumClickSize);
                var hitRect = new Rect(center - size * .5f, size);
                float distance = (screenPosition - center).sqrMagnitude;

                if (hitRect.Contains(screenPosition) && distance < closestDistance)
                {
                    closest = human;
                    closestDistance = distance;
                }
            }

            return closest;
        }


        // 화면 클릭 좌표에서 실제 건물 바닥 영역 조회
        internal CityBuilding GetBuildingAtScreenPosition(Vector2 screenPosition)
        {
            // 소환 모드 외 입력 차단과 카메라 외부 클릭 거부
            if (mapData == null || viewCamera == null || IsInputBlocked || !viewCamera.pixelRect.Contains(screenPosition))
            {
                return null;
            }

            // 지도 부모 변환 후 기존 구역 인덱스에서 바닥과 확장 지붕 조회
            Vector2 local = transform.InverseTransformPoint(viewCamera.ScreenToWorldPoint(screenPosition));
            return terrainRenderer != null ? terrainRenderer.GetBuildingAt(local) : null;
        }


        // 건물 정보창을 새 지붕 위에 배치할 표시 상단 조회
        internal float GetBuildingVisualTop(CityBuilding building)
        {
            // 필지 상단이 아닌 실제 지붕과 굴뚝 등 그려진 외형의 상단 사용
            return CityBuildingArt.GetVisualTop(terrainArt, building, mapData.Seed);
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
            renderer.sortingOrder = MapSortingOrder + UnitWalkAnimation.LivingSortingOffset;
            var animationView = unit.AddComponent<UnitWalkAnimation>();
            animationView.Initialize(renderer, GetUnitWalkFrames(zombie));

            if (animateSummon)
            {
                animationView.PlaySummon(GetUnitWalkFrames(zombie, true));
            }
            unit.AddComponent<UnitWander>().Initialize(mapData, zombie, index, GetUnitWalkFrames(true), GetUnitConversionFrames(),
                GetDeathVisuals(true), zombie ? null : GetDeathVisuals(false));
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
            int iconSize = LegacyPixelsPerTile * UnitPixelScale;
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
            int size = LegacyPixelsPerTile * UnitPixelScale;
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


        // 쓰러진 좀비 또는 아사한 인간의 흰색 전환 프레임과 먼지 픽셀 공용 자원 생성
        private UnitDeathVisuals GetDeathVisuals(bool zombie)
        {
            // 유형별로 모든 개체가 동일 사망 자원 공유
            var cached = zombie ? zombieDeathVisuals : humanDeathVisuals;

            if (cached != null)
            {
                return cached;
            }

            const int FrameCount = 51;
            int size = LegacyPixelsPerTile * UnitPixelScale;
            var body = new Color32[size * size];
            DrawPeople(body, size, new System.Collections.Generic.List<Vector2Int> { Vector2Int.zero }, zombie);
            var fadeFrames = new Sprite[FrameCount];

            for (int frame = 0; frame < FrameCount; frame++)
            {
                // 투명 배경을 유지하고 불투명 몸체만 원래 색상에서 흰색으로 보간
                float progress = frame / (float)(FrameCount - 1);
                var pixels = new Color32[body.Length];

                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = body[i].a > 0 ? (Color32)Color.Lerp(body[i], Color.white, progress) : body[i];
                }

                fadeFrames[frame] = CreatePointSprite(pixels, size, Vector2.zero, size);
            }

            // 화면 도트 한 칸 크기의 공용 흰색 먼지 픽셀 생성
            var pixelColor = new[] { new Color32(255, 255, 255, 255) };
            Sprite pixelSprite = CreatePointSprite(pixelColor, 1, new Vector2(.5f, .5f), LegacyPixelsPerTile);

            // 확대 격자 한 칸을 원본 도트 한 개로 묶어 몸체 픽셀 중심 좌표 수집
            var consumed = new bool[body.Length];
            var dustPoints = new System.Collections.Generic.List<Vector2>();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (body[y * size + x].a == 0 || consumed[y * size + x])
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

            cached = new UnitDeathVisuals(fadeFrames, pixelSprite, dustPoints.ToArray(), 1f / LegacyPixelsPerTile);

            if (zombie)
            {
                zombieDeathVisuals = cached;
            }
            else
            {
                humanDeathVisuals = cached;
            }

            return cached;
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


        // 사망 전용 프레임과 먼지 픽셀 자원 해제
        private void ReleaseDeathVisuals(UnitDeathVisuals visuals)
        {
            // 아직 생성하지 않은 사망 자원 정리 생략
            if (visuals == null)
            {
                return;
            }

            foreach (var frame in visuals.FadeFrames)
            {
                Destroy(frame.texture);
                Destroy(frame);
            }

            if (visuals.PixelSprite != null)
            {
                Destroy(visuals.PixelSprite.texture);
                Destroy(visuals.PixelSprite);
            }
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

            if (terrainRenderer != null)
            {
                terrainRenderer.Dispose();
                terrainRenderer = null;
            }

            DestroyOverviews(overviewTextures, overviewSprites);
            overviewTextures = null;
            overviewSprites = null;
        }


        // 씬 종료 시 소유 자원 정리
        private void OnDestroy()
        {
            // 씬 종료 시 생성한 그래픽 자원의 수명 종료
            ReleaseGraphics();

            // 유형별 걷기 전용 프레임과 공용 아이콘의 소유 자원 해제
            ReleaseConversionFrames();
            ReleaseDeathVisuals(zombieDeathVisuals);
            ReleaseDeathVisuals(humanDeathVisuals);
            zombieDeathVisuals = null;
            humanDeathVisuals = null;
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
