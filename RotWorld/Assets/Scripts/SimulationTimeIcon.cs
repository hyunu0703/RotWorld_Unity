using UnityEngine;
using UnityEngine.UI;

namespace RotWorld
{
    /// <summary>
    /// 시간 제어의 실행 및 정지와 배속별 벡터 아이콘 메시 구성
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SimulationTimeIcon : MaskableGraphic
    {
        // 실행 및 정지와 배속의 도형 선택
        internal enum Shape { Play, Pause, FastForward, TripleFastForward, Lightning, Stop }
        private Shape shape;

        // 화면 픽셀 기준 삼각형 경계 완화 폭과 도형 배치 비율
        private const float EdgeWidth = .75f;
        private const float TriangleWidthRatio = .45f;
        private const float PlayWidthRatio = .6f;
        private const float TriangleGapRatio = .1f;
        private const float PauseBarInset = 5f / 24f;
        private const float PauseBarWidth = 5f / 24f;

        // 뾰족한 양 끝과 중앙 꺾임을 가진 세로형 번개 윤곽 및 내부 삼각형 연결
        private static readonly Vector2[] LightningPoints =
        {
            new Vector2(.8f, 1f), new Vector2(0f, .4f), new Vector2(.5f, .4f),
            new Vector2(.2f, 0f), new Vector2(1f, .6f), new Vector2(.5f, .6f)
        };
        private static readonly int[] LightningTriangles = { 0, 1, 2, 0, 2, 5, 2, 3, 4, 2, 4, 5 };

        // 시간 버튼에서 전달한 도형 선택 및 메시 갱신 요청
        internal void Initialize(Shape shape)
        {
            // 렌더러 생성이 끝난 상태에서 VertexHelper 기반 메시 경로와 표시 색상 설정
            useLegacyMeshGeneration = false;
            this.shape = shape;
            color = Color.white;
            canvasRenderer.SetColor(Color.white);

            // 아이콘 메시와 기본 UI 머티리얼을 함께 갱신하여 최초 표시 보장
            SetAllDirty();
        }


        // 기존 선택 색상을 유지하며 표시 도형만 변경
        internal void SetShape(Shape shape)
        {
            // 동일 도형의 반복 메시 갱신 생략
            if (this.shape == shape)
            {
                return;
            }

            this.shape = shape;
            SetVerticesDirty();
        }


        // UI 사각형 크기에 맞춘 실행 및 정지와 배속 메시 구성
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            // 이전 메시 제거 및 레이아웃 확정 전 빈 영역 처리
            vertices.Clear();
            var bounds = rectTransform.rect;

            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            // 선택한 실행 상태와 배속에 대응하는 벡터 도형 구성
            if (shape == Shape.Pause)
            {
                float barWidth = bounds.width * PauseBarWidth;
                float inset = bounds.width * PauseBarInset;
                AddBar(vertices, bounds.xMin + inset, bounds.yMin, barWidth, bounds.height);
                AddBar(vertices, bounds.xMax - inset - barWidth, bounds.yMin, barWidth, bounds.height);
            }
            else if (shape == Shape.FastForward)
            {
                float triangleWidth = bounds.width * TriangleWidthRatio;
                AddTriangle(vertices, bounds.xMin, bounds.yMin, triangleWidth, bounds.height);
                AddTriangle(vertices, bounds.xMax - triangleWidth, bounds.yMin, triangleWidth, bounds.height);
            }
            else if (shape == Shape.TripleFastForward)
            {
                const int TriangleCount = 3;
                float gap = bounds.width * TriangleGapRatio;
                float triangleWidth = (bounds.width - gap * (TriangleCount - 1)) / TriangleCount;

                for (int i = 0; i < TriangleCount; i++)
                {
                    AddTriangle(vertices, bounds.xMin + i * (triangleWidth + gap), bounds.yMin, triangleWidth, bounds.height);
                }
            }
            else if (shape == Shape.Lightning)
            {
                AddLightning(vertices, bounds);
            }
            else if (shape == Shape.Stop)
            {
                float side = Mathf.Min(bounds.width, bounds.height);
                AddBar(vertices, bounds.center.x - side * .5f, bounds.center.y - side * .5f, side, side);
            }
            else
            {
                float triangleWidth = bounds.width * PlayWidthRatio;
                AddTriangle(vertices, bounds.center.x - triangleWidth * .5f, bounds.yMin, triangleWidth, bounds.height);
            }
        }


        // 정규화한 번개 윤곽을 아이콘 영역의 벡터 메시로 변환
        private void AddLightning(VertexHelper vertices, Rect bounds)
        {
            // 아이콘 크기에 맞춘 윤곽 정점과 오목한 부분을 가로지르지 않는 면 추가
            int start = vertices.currentVertCount;

            for (int i = 0; i < LightningPoints.Length; i++)
            {
                Vector2 point = bounds.min + Vector2.Scale(LightningPoints[i], bounds.size);
                vertices.AddVert(point, color, Vector2.zero);
            }

            for (int i = 0; i < LightningTriangles.Length; i += 3)
            {
                vertices.AddTriangle(start + LightningTriangles[i], start + LightningTriangles[i + 1], start + LightningTriangles[i + 2]);
            }
        }


        // 오른쪽을 향하는 삼각형과 투명도 경계 메시 추가
        private void AddTriangle(VertexHelper vertices, float left, float bottom, float width, float height)
        {
            // 삼각형 내심을 기준으로 화면 배율에 맞는 균일한 경계 확장
            var a = new Vector2(left, bottom);
            var b = new Vector2(left, bottom + height);
            var c = new Vector2(left + width, bottom + height * .5f);
            float side = Vector2.Distance(a, c);
            float perimeter = height + side * 2f;
            Vector2 center = (a * side + b * side + c * height) / perimeter;
            float radius = width * height / perimeter;
            float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            float expansion = 1f + EdgeWidth / Mathf.Max(scaleFactor, Mathf.Epsilon) / radius;
            Color32 transparent = color;
            transparent.a = 0;
            int start = vertices.currentVertCount;

            // 불투명 본체와 화면 약 1픽셀 폭의 투명 경계를 별도 정점으로 연결
            vertices.AddVert(a, color, Vector2.zero);
            vertices.AddVert(b, color, Vector2.zero);
            vertices.AddVert(c, color, Vector2.zero);
            vertices.AddVert(center + (a - center) * expansion, transparent, Vector2.zero);
            vertices.AddVert(center + (b - center) * expansion, transparent, Vector2.zero);
            vertices.AddVert(center + (c - center) * expansion, transparent, Vector2.zero);
            vertices.AddTriangle(start, start + 1, start + 2);

            for (int i = 0; i < 3; i++)
            {
                int next = (i + 1) % 3;
                vertices.AddTriangle(start + i, start + i + 3, start + next + 3);
                vertices.AddTriangle(start + i, start + next + 3, start + next);
            }
        }


        // 정지 아이콘의 직사각형 막대 메시 추가
        private void AddBar(VertexHelper vertices, float left, float bottom, float width, float height)
        {
            // 현재 정점 뒤에 막대의 네 모서리와 삼각형 두 개 추가
            int start = vertices.currentVertCount;
            vertices.AddVert(new Vector2(left, bottom), color, Vector2.zero);
            vertices.AddVert(new Vector2(left, bottom + height), color, Vector2.zero);
            vertices.AddVert(new Vector2(left + width, bottom + height), color, Vector2.zero);
            vertices.AddVert(new Vector2(left + width, bottom), color, Vector2.zero);
            vertices.AddTriangle(start, start + 1, start + 2);
            vertices.AddTriangle(start + 2, start + 3, start);
        }
    }
}
