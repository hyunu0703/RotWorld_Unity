using UnityEngine;
using UnityEngine.UI;

namespace RotWorld
{
    /// <summary>
    /// 선택한 유닛의 발밑을 둘러싸는 비어 있는 타원 테두리 표시
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SelectionEllipseGraphic : MaskableGraphic
    {
        // 타원 분할 정밀도와 캔버스 기준 테두리 두께
        private const int SegmentCount = 48;
        private const float BorderThickness = 1.5f;

        // VertexHelper 기반 메시 생성 경로를 활성화한 UI 컴포넌트 초기화
        public SelectionEllipseGraphic()
        {
            // 기본 Graphic의 구형 메시 경로 대신 타원 정점 생성 경로 사용
            useLegacyMeshGeneration = false;
        }


        // 현재 표시 크기에 맞는 타원 테두리 메시 생성
        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            // 기존 메시 제거 후 비어 있는 표시 영역 처리 생략
            vertexHelper.Clear();
            Rect bounds = rectTransform.rect;

            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            // 바깥 타원과 안쪽 타원 사이에만 정점 배치
            Vector2 outerRadius = bounds.size * .5f;
            Vector2 innerRadius = new Vector2(Mathf.Max(0f, outerRadius.x - BorderThickness),
                Mathf.Max(0f, outerRadius.y - BorderThickness));

            for (int i = 0; i < SegmentCount; i++)
            {
                float angle = i * Mathf.PI * 2f / SegmentCount;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertexHelper.AddVert(bounds.center + Vector2.Scale(direction, outerRadius), color, Vector2.zero);
                vertexHelper.AddVert(bounds.center + Vector2.Scale(direction, innerRadius), color, Vector2.zero);
            }

            // 마지막 구간을 시작 구간에 연결해 끊김 없는 테두리 구성
            for (int i = 0; i < SegmentCount; i++)
            {
                int outer = i * 2;
                int nextOuter = (i + 1) % SegmentCount * 2;
                vertexHelper.AddTriangle(outer, nextOuter, outer + 1);
                vertexHelper.AddTriangle(outer + 1, nextOuter, nextOuter + 1);
            }
        }
    }
}
