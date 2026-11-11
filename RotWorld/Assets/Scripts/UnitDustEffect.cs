using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 사라지는 유닛의 몸체 픽셀을 흩날리는 먼지로 표시 후 자체 제거
    /// </summary>
    public sealed class UnitDustEffect : MonoBehaviour
    {
        // 픽셀별 출발 지연과 수명 범위
        private const float MaximumDelay = .35f;
        private const float MinimumLifetime = .6f;
        private const float MaximumLifetime = 1.2f;

        // 위로 떠오르며 옆으로 흩어지는 속도 범위
        private const float MinimumRiseSpeed = .25f;
        private const float MaximumRiseSpeed = .7f;
        private const float MaximumDriftSpeed = .45f;
        private const float DriftDamping = 1.5f;

        // 개체별 먼지 픽셀 표시와 이동 상태
        private SpriteRenderer[] pixels;
        private Vector2[] origins;
        private Vector2[] offsets;
        private Vector2[] velocities;
        private float[] delays;
        private float[] lifetimes;
        private float pixelSize;
        private float elapsed;
        private float duration;

        // 시작 좌표마다 한 픽셀 먼지 생성
        public void Play(Sprite pixelSprite, Vector3[] startPositions, float pixelSize, int sortingOrder)
        {
            // 표시 자원이나 시작 좌표가 없으면 빈 연출 개체 제거
            if (pixelSprite == null || startPositions == null || startPositions.Length == 0 || pixelSize <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // 픽셀별 상태 배열 할당
            int count = startPositions.Length;
            this.pixelSize = pixelSize;
            pixels = new SpriteRenderer[count];
            origins = new Vector2[count];
            offsets = new Vector2[count];
            velocities = new Vector2[count];
            delays = new float[count];
            lifetimes = new float[count];
            duration = 0f;

            for (int i = 0; i < count; i++)
            {
                // 공용 한 픽셀 스프라이트로 개별 렌더러 생성
                var pixel = new GameObject("Dust Pixel");
                pixel.transform.SetParent(transform, false);
                pixel.transform.position = startPositions[i];
                var renderer = pixel.AddComponent<SpriteRenderer>();
                renderer.sprite = pixelSprite;
                renderer.sortingOrder = sortingOrder;
                pixels[i] = renderer;

                // 무작위 지연과 속도로 동시에 흩어지지 않고 순차적으로 떠오르는 먼지 구성
                origins[i] = pixel.transform.localPosition;
                velocities[i] = new Vector2(Random.Range(-MaximumDriftSpeed, MaximumDriftSpeed), Random.Range(MinimumRiseSpeed, MaximumRiseSpeed));
                delays[i] = Random.Range(0f, MaximumDelay);
                lifetimes[i] = Random.Range(MinimumLifetime, MaximumLifetime);
                duration = Mathf.Max(duration, delays[i] + lifetimes[i]);
            }
        }


        // 픽셀 이동과 투명도 감소 후 전체 연출 종료
        private void Update()
        {
            // 재생 전 상태 처리 생략
            if (pixels == null)
            {
                return;
            }

            elapsed += Time.deltaTime;

            for (int i = 0; i < pixels.Length; i++)
            {
                float time = elapsed - delays[i];

                if (time <= 0f || !pixels[i].enabled)
                {
                    continue;
                }

                float progress = time / lifetimes[i];

                if (progress >= 1f)
                {
                    pixels[i].enabled = false;
                    continue;
                }

                // 좌우 흩어짐은 점차 줄이고 상승은 유지해 먼지처럼 떠오름 표현
                velocities[i].x *= Mathf.Exp(-DriftDamping * Time.deltaTime);
                offsets[i] += velocities[i] * Time.deltaTime;

                // 시작점 기준 이동량을 픽셀 단위로 끊어 도트 그래픽 규격 유지
                Vector2 snapped = new Vector2(Mathf.Round(offsets[i].x / pixelSize), Mathf.Round(offsets[i].y / pixelSize)) * pixelSize;
                pixels[i].transform.localPosition = origins[i] + snapped;
                pixels[i].color = new Color(1f, 1f, 1f, 1f - progress);
            }

            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
