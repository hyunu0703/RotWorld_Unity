using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 거점 영역의 반투명 채움과 화면 두께 고정 외곽선 지도 표시
    /// </summary>
    internal sealed class SettlementBaseRenderer : IDisposable
    {
        // 지형 구역 위와 건물·유닛 깊이 정렬 시작 순서 아래의 표시 순서
        private const int FillSortingOrder = 1;
        private const int OutlineSortingOrder = 2;

        // 채움은 투명도 높게, 외곽선은 투명도 낮게 표시할 알파
        private const float FillAlpha = .22f;
        private const float OutlineAlpha = .9f;

        // 줌과 무관한 화면상 외곽선 두께
        private const float OutlinePixels = 2f;
        private const int OutlineSides = 4;

        /// <summary>
        /// 거점 하나의 채움과 네 변 외곽선 표시 객체
        /// </summary>
        private sealed class AreaView
        {
            public GameObject Root;
            public SpriteRenderer Fill;
            public readonly SpriteRenderer[] Sides = new SpriteRenderer[OutlineSides];
            public RectInt Area;
        }

        // 표시 대상 지도와 부모 및 공용 흰색 스프라이트
        private readonly CityMap map;
        private readonly Transform root;
        private readonly Texture2D pixelTexture;
        private readonly Sprite pixelSprite;

        // 사용 중 영역 표시와 재사용 대기 표시
        private readonly List<AreaView> activeViews = new List<AreaView>();
        private readonly Stack<AreaView> availableViews = new Stack<AreaView>();

        // 마지막 반영 거점 목록 번호와 외곽선 두께
        private int displayedAreaVersion = -1;
        private float displayedOutlineWidth = -1f;
        private bool isDisposed;


        // 지도 표시 부모 아래에 거점 표시 루트와 공용 스프라이트 생성
        internal SettlementBaseRenderer(CityMap map, Transform parent)
        {
            // 거점 목록 출처와 지도 좌표계 부모 확인
            this.map = map ?? throw new ArgumentNullException(nameof(map));

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            // 지도 타일 좌표와 같은 단위로 늘려 쓸 1×1 흰색 픽셀 스프라이트
            root = new GameObject("Settlement Bases").transform;
            root.SetParent(parent, false);
            pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixelTexture.name = "Settlement base pixel";
            pixelTexture.filterMode = FilterMode.Point;
            pixelTexture.SetPixel(0, 0, Color.white);
            pixelTexture.Apply(false, true);
            pixelSprite = Sprite.Create(pixelTexture, new Rect(0, 0, 1, 1), Vector2.zero, 1f, 0, SpriteMeshType.FullRect);
        }


        // 거점 목록 변경과 줌 변경 시에만 영역 표시 갱신
        internal void Refresh(Camera camera)
        {
            // 해제 이후 접근과 카메라 누락 시 생략
            if (isDisposed || camera == null)
            {
                return;
            }

            // 거점 생성·해제가 있을 때만 표시 객체 재배정
            if (displayedAreaVersion != map.Bases.AreaVersion)
            {
                displayedAreaVersion = map.Bases.AreaVersion;
                RebuildViews();
                displayedOutlineWidth = -1f;
            }

            // 현재 배율의 화면 2픽셀을 지도 단위로 환산해 바뀐 경우만 외곽선 두께 갱신
            float pixelsPerUnit = Mathf.Max(1, camera.pixelHeight) / (2f * Mathf.Max(.0001f, camera.orthographicSize));
            float outlineWidth = OutlinePixels / pixelsPerUnit;

            if (Mathf.Approximately(outlineWidth, displayedOutlineWidth))
            {
                return;
            }

            displayedOutlineWidth = outlineWidth;

            foreach (var view in activeViews)
            {
                PlaceOutline(view, outlineWidth);
            }
        }


        // 현재 거점 목록에 맞춰 표시 객체 반환과 배정 및 채움 배치
        private void RebuildViews()
        {
            // 모든 표시를 대기 목록으로 반환 후 거점 순서대로 다시 배정
            for (int i = activeViews.Count - 1; i >= 0; i--)
            {
                activeViews[i].Root.SetActive(false);
                availableViews.Push(activeViews[i]);
            }

            activeViews.Clear();

            foreach (var settlementBase in map.Bases.All)
            {
                AreaView view = availableViews.Count > 0 ? availableViews.Pop() : CreateView();
                RectInt area = settlementBase.Area;
                Color color = settlementBase.Color;
                view.Area = area;

                // 영역 전체를 거점 색의 높은 투명도로 채움
                view.Fill.color = new Color(color.r, color.g, color.b, FillAlpha);
                view.Fill.transform.localPosition = new Vector3(area.x, area.y, 0f);
                view.Fill.transform.localScale = new Vector3(area.width, area.height, 1f);

                // 외곽선은 같은 색의 낮은 투명도
                var outlineColor = new Color(color.r, color.g, color.b, OutlineAlpha);

                foreach (var side in view.Sides)
                {
                    side.color = outlineColor;
                }

                view.Root.SetActive(true);
                activeViews.Add(view);
            }
        }


        // 영역 안쪽 가장자리에 네 변 외곽선 배치
        private static void PlaceOutline(AreaView view, float width)
        {
            // 작은 영역에서도 변이 겹쳐 넘치지 않게 두께를 영역 절반 이하로 제한
            RectInt area = view.Area;
            float thickness = Mathf.Min(width, area.width * .5f, area.height * .5f);

            // 아래·위 변은 가로 전체, 왼쪽·오른쪽 변은 위아래 변 사이만 차지
            SetRect(view.Sides[0], area.x, area.y, area.width, thickness);
            SetRect(view.Sides[1], area.x, area.yMax - thickness, area.width, thickness);
            SetRect(view.Sides[2], area.x, area.y + thickness, thickness, area.height - thickness * 2f);
            SetRect(view.Sides[3], area.xMax - thickness, area.y + thickness, thickness, area.height - thickness * 2f);
        }


        // 1×1 스프라이트를 지도 좌표 사각형으로 배치
        private static void SetRect(SpriteRenderer renderer, float x, float y, float width, float height)
        {
            // 스프라이트 기준점이 왼쪽 아래이므로 위치와 크기만 지정
            renderer.transform.localPosition = new Vector3(x, y, 0f);
            renderer.transform.localScale = new Vector3(width, height, 1f);
        }


        // 채움 하나와 외곽선 네 변으로 구성한 표시 객체 생성
        private AreaView CreateView()
        {
            // 거점마다 같은 구조를 만들고 이후 반환·재사용
            var view = new AreaView { Root = new GameObject("Settlement Base") };
            view.Root.transform.SetParent(root, false);
            view.Fill = CreateRenderer(view.Root.transform, "Fill", FillSortingOrder);

            for (int i = 0; i < OutlineSides; i++)
            {
                view.Sides[i] = CreateRenderer(view.Root.transform, "Outline", OutlineSortingOrder);
            }

            return view;
        }


        // 공용 흰색 스프라이트를 쓰는 표시 렌더러 생성
        private SpriteRenderer CreateRenderer(Transform parent, string name, int sortingOrder)
        {
            // 색은 배정 시 지정하고 정렬 순서만 고정
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = pixelSprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }


        // 표시 객체와 공용 스프라이트 자원 해제
        public void Dispose()
        {
            // 중복 해제 방지
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            activeViews.Clear();
            availableViews.Clear();

            if (root != null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
            }

            UnityEngine.Object.Destroy(pixelSprite);
            UnityEngine.Object.Destroy(pixelTexture);
        }
    }
}
