using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 화면 주변 지형과 도로 구역의 지연 생성 및 상한 캐시와 기존 건물 합성
    /// </summary>
    internal sealed class CityChunkRenderer : IDisposable
    {
        // 구역 크기와 프레임 생성량 및 GPU 텍스처 캐시 상한
        internal const int ChunkTiles = 32;
        internal const int MaximumCachedChunks = 64;
        private const int ChunksPerFrame = 2;
        private const int LegacyPixelsPerTile = 8;
        private const int PixelScale = CityArtCatalog.PixelsPerTile / LegacyPixelsPerTile;
        private const int ChunkPixels = ChunkTiles * CityArtCatalog.PixelsPerTile;

        // 고정 지도와 지형 참조 및 생성 구역의 부모
        private readonly CityMap map;
        private readonly CityArtCatalog art;
        private readonly Transform parent;
        private readonly int sortingOrder;

        // 구역별 표시 자원과 건물 후보 및 오래된 캐시 순서
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, List<CityBuilding>> buildings = new Dictionary<Vector2Int, List<CityBuilding>>();
        private long accessOrder;
        private bool isDisposed;

        /// <summary>
        /// 단일 구역의 런타임 표시 자원과 최근 사용 순서 보관
        /// </summary>
        private sealed class Chunk
        {
            // 구역에서만 소유하는 Unity 자원과 캐시 접근 순위
            internal GameObject Object;
            internal Texture2D Texture;
            internal Sprite Sprite;
            internal long LastUse;
        }


        // 고정 지도와 카탈로그 연결 및 구역별 건물 후보 인덱스 구성
        internal CityChunkRenderer(CityMap map, CityArtCatalog art, Transform parent, int sortingOrder)
        {
            // 필수 자원 검증 후 카메라 조회와 독립적인 고정 데이터 보관
            if (map == null || art == null || parent == null)
            {
                throw new ArgumentNullException("지형 렌더러의 지도 또는 아트 또는 부모 누락");
            }

            art.Prepare();
            this.map = map;
            this.art = art;
            this.parent = parent;
            this.sortingOrder = sortingOrder;

            foreach (var building in map.Buildings)
            {
                // 기존 우하단 그림자까지 포함하는 인접 구역 후보 등록
                int minX = Mathf.Max(0, building.Bounds.xMin - 1) / ChunkTiles;
                int minY = Mathf.Max(0, building.Bounds.yMin - 1) / ChunkTiles;
                int maxX = Mathf.Min(map.Size - 1, building.Bounds.xMax + 1) / ChunkTiles;
                int maxY = Mathf.Min(map.Size - 1, building.Bounds.yMax + 1) / ChunkTiles;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var key = new Vector2Int(x, y);

                        if (!buildings.TryGetValue(key, out List<CityBuilding> candidates))
                        {
                            candidates = new List<CityBuilding>();
                            buildings.Add(key, candidates);
                        }

                        candidates.Add(building);
                    }
                }
            }
        }


        // 카메라 주변 구역의 활성 상태와 제한된 지연 생성 갱신
        internal void UpdateVisible(Camera camera, bool showDetail)
        {
            // 해제 이후 접근 방지와 지도 안의 카메라 가시 구역 계산
            if (isDisposed || camera == null)
            {
                return;
            }

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            Vector3 center = camera.transform.position;
            int count = (map.Size + ChunkTiles - 1) / ChunkTiles;
            int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - halfWidth) / ChunkTiles) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt((center.y - halfHeight) / ChunkTiles) - 1);
            int maxX = Mathf.Min(count - 1, Mathf.FloorToInt((center.x + halfWidth) / ChunkTiles) + 1);
            int maxY = Mathf.Min(count - 1, Mathf.FloorToInt((center.y + halfHeight) / ChunkTiles) + 1);
            var visible = new RectInt(minX, minY, Mathf.Max(0, maxX - minX + 1), Mathf.Max(0, maxY - minY + 1));

            // 큰 시야에서는 축소 지도를 사용해 캐시 상한과 부분 상세 패치 방지
            bool useChunks = showDetail && visible.width * visible.height <= MaximumCachedChunks;

            foreach (var pair in chunks)
            {
                bool isVisible = useChunks && visible.Contains(pair.Key);
                if (pair.Value.Object.activeSelf != isVisible)
                {
                    pair.Value.Object.SetActive(isVisible);
                }

                if (isVisible)
                {
                    pair.Value.LastUse = ++accessOrder;
                }
            }

            if (!useChunks)
            {
                return;
            }

            int created = 0;

            // 화면 중앙부터 구역을 준비하고 나머지 영역은 기존 축소 지도로 표시
            int centerX = Mathf.Clamp(Mathf.FloorToInt(center.x / ChunkTiles), minX, maxX);
            int centerY = Mathf.Clamp(Mathf.FloorToInt(center.y / ChunkTiles), minY, maxY);
            int radius = Mathf.Max(visible.width, visible.height);

            for (int ring = 0; ring < radius && created < ChunksPerFrame; ring++)
            {
                for (int y = Mathf.Max(minY, centerY - ring); y <= Mathf.Min(maxY, centerY + ring) && created < ChunksPerFrame; y++)
                {
                    for (int x = Mathf.Max(minX, centerX - ring); x <= Mathf.Min(maxX, centerX + ring) && created < ChunksPerFrame; x++)
                    {
                        var key = new Vector2Int(x, y);

                        if (Mathf.Max(Mathf.Abs(x - centerX), Mathf.Abs(y - centerY)) != ring || chunks.ContainsKey(key))
                        {
                            continue;
                        }

                        EvictOldest(visible);
                        Chunk chunk = CreateChunk(key);
                        chunk.LastUse = ++accessOrder;
                        chunks.Add(key, chunk);
                        created++;
                    }
                }
            }
        }


        // 캐시 상한 도달 시 화면 밖에서 가장 오래 사용하지 않은 구역 해제
        private void EvictOldest(RectInt visible)
        {
            // 현재 화면의 구역은 교체 대상에서 제외
            if (chunks.Count < MaximumCachedChunks)
            {
                return;
            }

            Vector2Int oldestKey = default;
            Chunk oldest = null;

            foreach (var pair in chunks)
            {
                if (!visible.Contains(pair.Key) && (oldest == null || pair.Value.LastUse < oldest.LastUse))
                {
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                }
            }

            if (oldest == null)
            {
                throw new InvalidOperationException("가시 구역 수가 지형 캐시 상한을 초과했습니다");
            }

            ReleaseChunk(oldest);
            chunks.Remove(oldestKey);
        }


        // 단일 512픽셀 구역의 지형과 기존 시설을 생성해 렌더러 연결
        private Chunk CreateChunk(Vector2Int key)
        {
            // 생성 도중 실패해도 해당 구역의 부분 자원 반환
            var chunk = new Chunk();

            try
            {
                chunk.Texture = new Texture2D(ChunkPixels, ChunkPixels, TextureFormat.RGBA32, false);
                chunk.Texture.name = $"City terrain {key.x},{key.y}";
                chunk.Texture.filterMode = FilterMode.Point;
                chunk.Texture.wrapMode = TextureWrapMode.Clamp;
                PaintChunk(key, chunk.Texture.GetRawTextureData<Color32>());
                chunk.Texture.Apply(false, true);
                chunk.Sprite = Sprite.Create(chunk.Texture, new Rect(0, 0, ChunkPixels, ChunkPixels),
                    Vector2.zero, CityArtCatalog.PixelsPerTile, 0, SpriteMeshType.FullRect);
                chunk.Object = new GameObject(chunk.Texture.name);
                chunk.Object.transform.SetParent(parent, false);
                chunk.Object.transform.localPosition = new Vector3(key.x * ChunkTiles, key.y * ChunkTiles, 0f);
                var renderer = chunk.Object.AddComponent<SpriteRenderer>();
                renderer.sprite = chunk.Sprite;
                renderer.sortingOrder = sortingOrder;
                return chunk;
            }
            catch
            {
                ReleaseChunk(chunk);
                throw;
            }
        }


        // 원본 지형과 도로를 16픽셀 격자로 합성한 뒤 기존 건물 표시
        private void PaintChunk(Vector2Int key, NativeArray<Color32> pixels)
        {
            // 구역 경계를 넘어도 같은 전역 좌표와 이웃을 사용하는 지형 선택
            int originX = key.x * ChunkTiles;
            int originY = key.y * ChunkTiles;

            for (int localY = 0; localY < ChunkTiles && originY + localY < map.Size; localY++)
            {
                for (int localX = 0; localX < ChunkTiles && originX + localX < map.Size; localX++)
                {
                    int x = originX + localX;
                    int y = originY + localY;
                    int index = y * map.Size + x;
                    byte tile = map.Tiles[index];
                    bool isTerrain = tile == CityGenerator.Grass || tile == CityGenerator.Sand ||
                        tile == CityGenerator.Ocean || tile == CityGenerator.Shallows || tile == CityGenerator.River;
                    bool isStreet = CityArtCatalog.IsStreet(tile);
                    int tileIndex = art.GetTileIndex(map, x, y);
                    int coast = isTerrain ? art.GetCoastMask(map, x, y) : 0;
                    CityArtCatalog.StreetTile street = isStreet ? art.GetStreetTile(map, x, y) : default;

                    for (int py = 0; py < CityArtCatalog.PixelsPerTile; py++)
                    {
                        for (int px = 0; px < CityArtCatalog.PixelsPerTile; px++)
                        {
                            Color32 color = isTerrain
                                ? art.Sample(tileIndex, map.Regions[index], tile == CityGenerator.Shallows || tile == CityGenerator.River, coast, px, py)
                                : isStreet ? art.SampleStreet(street, px, py) : Hex(0x91978c);
                            pixels[(localY * CityArtCatalog.PixelsPerTile + py) * ChunkPixels + localX * CityArtCatalog.PixelsPerTile + px] = color;
                        }
                    }
                }
            }

            if (buildings.TryGetValue(key, out List<CityBuilding> candidates))
            {
                foreach (var building in candidates)
                {
                    DrawBuilding(pixels, ChunkPixels, building, originX, originY);
                }
            }
        }


        // 단일 구역의 표시 참조를 끊고 소유 Unity 자원 해제
        private static void ReleaseChunk(Chunk chunk)
        {
            // 프레임 끝 파괴 전 표시 비활성화로 교체 프레임 중복 방지
            if (chunk.Object != null)
            {
                chunk.Object.SetActive(false);
                UnityEngine.Object.Destroy(chunk.Object);
                chunk.Object = null;
            }

            if (chunk.Sprite != null)
            {
                UnityEngine.Object.Destroy(chunk.Sprite);
                chunk.Sprite = null;
            }

            if (chunk.Texture != null)
            {
                UnityEngine.Object.Destroy(chunk.Texture);
                chunk.Texture = null;
            }
        }


        // 지도 교체와 소유 뷰 종료 시 전체 구역 및 건물 인덱스 반환
        public void Dispose()
        {
            // 반복 해제를 허용하고 생성 구역만 정리
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            foreach (var chunk in chunks.Values)
            {
                ReleaseChunk(chunk);
            }

            chunks.Clear();
            buildings.Clear();
        }


        // 기존 건물의 색상과 창문 및 출입구 도형을 구역 좌표로 변환
        internal static void DrawBuilding(NativeArray<Color32> pixels, int stride, CityBuilding building, int originX, int originY, float pixelScale = PixelScale)
        {
            // 건물 필지를 픽셀 좌표로 환산한 뒤 외형과 출입구 표시
            var r = building.Bounds;
            int x = (r.x - originX) * LegacyPixelsPerTile;
            int y = (r.y - originY) * LegacyPixelsPerTile;
            int w = r.width * LegacyPixelsPerTile;
            int h = r.height * LegacyPixelsPerTile;
            Color32 roof = building.Kind == 0 ? Hex(0x9d604b) : building.Kind == 1 ? Hex(0x64848b)
                : building.Kind == 3 ? Hex(0x596977) : building.Kind == 4 ? Hex(0x789b96) : Hex(0x8a8890);
            Fill(pixels, stride, x + 2, y - 2, w, h, Hex(0x424b40), pixelScale);
            Fill(pixels, stride, x, y, w, h, Hex(0x353e3f), pixelScale);
            Fill(pixels, stride, x + 1, y + 1, w - 2, 6, Hex(0xc1b494), pixelScale);
            Fill(pixels, stride, x + 1, y + 7, w - 2, h - 8, roof, pixelScale);
            Fill(pixels, stride, x + 3, y + h - 3, w - 6, 1, Hex(0xc2b194), pixelScale);

            for (int wx = x + 5; wx < x + w - 4; wx += 9)
            {
                Fill(pixels, stride, wx, y + 2, 3, 3, Hex(0x465c67), pixelScale);
            }

            if (building.Kind == 0)
            {
                Fill(pixels, stride, x + 2, y + h / 2, w - 4, 2, Hex(0x794c40), pixelScale);
                Fill(pixels, stride, x + w - 10, y + h - 10, 4, 6, Hex(0x555453), pixelScale);
            }
            else
            {
                Fill(pixels, stride, x + w / 2, y + h / 2, 7, 5, Hex(0xb4b8ad), pixelScale);
                Fill(pixels, stride, x + 3, y + 7, w - 6, 2, building.Kind == 1 ? Hex(0xb69a56) : Hex(0x666975), pixelScale);
            }

            if (building.Kind == 3)
            {
                for (int floorY = y + 9; floorY < y + h - 3; floorY += 6)
                {
                    for (int wx = x + 4; wx < x + w - 3; wx += 6)
                    {
                        Fill(pixels, stride, wx, floorY, 2, 2, Hex(0xb8c5bc), pixelScale);
                    }
                }
            }

            // 생성 데이터의 실제 출입구 위치를 지붕 위에 표시
            int doorX = (building.Door.x - originX) * LegacyPixelsPerTile;
            int doorY = (building.Door.y - originY) * LegacyPixelsPerTile;
            Fill(pixels, stride, doorX + 2, doorY + 2, 4, 4, Hex(0xd1c6a0), pixelScale);
        }


        // 기존 시설 사각형을 출력 격자로 변환해 경계 안에 채우기
        private static void Fill(NativeArray<Color32> pixels, int stride, int x, int y, int width, int height, Color32 color, float pixelScale)
        {
            // 원본 8픽셀 격자의 영역을 출력 픽셀 중심에서 샘플링
            int minX = Mathf.Max(0, Mathf.CeilToInt(x * pixelScale - .5f));
            int minY = Mathf.Max(0, Mathf.CeilToInt(y * pixelScale - .5f));
            int maxX = Mathf.Min(stride, Mathf.CeilToInt((x + width) * pixelScale - .5f));
            int maxY = Mathf.Min(stride, Mathf.CeilToInt((y + height) * pixelScale - .5f));

            for (int py = minY; py < maxY; py++)
            {
                for (int px = minX; px < maxX; px++)
                {
                    pixels[py * stride + px] = color;
                }
            }
        }


        // 기존 시설 팔레트의 RGB 정수값 변환
        private static Color32 Hex(int rgb)
        {
            // RGB 정수의 채널 분리와 불투명 알파 적용
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }
    }
}
