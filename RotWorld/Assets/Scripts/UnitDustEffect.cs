using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 사라지는 유닛의 몸체 픽셀을 흩날리는 먼지로 표시 후 자체 제거
    /// </summary>
    public sealed class UnitDustEffect : MonoBehaviour
    {
        // 지도 부모 수명 안에서 재사용하는 완료 연출과 시작 좌표
        private static readonly List<UnitDustEffect> AvailableEffects = new List<UnitDustEffect>();
        private Vector3[] startPositions;

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

        // 같은 지도에서 완료된 먼지 연출 대여
        internal static UnitDustEffect Acquire(Transform parent)
        {
            // 다른 지도의 표시 자원과 수명을 섞지 않는 부모별 재사용
            for (int i = AvailableEffects.Count - 1; i >= 0; i--)
            {
                UnitDustEffect effect = AvailableEffects[i];

                if (effect == null)
                {
                    AvailableEffects.RemoveAt(i);
                    continue;
                }

                if (effect.transform.parent == parent)
                {
                    AvailableEffects.RemoveAt(i);
                    effect.gameObject.SetActive(true);
                    return effect;
                }
            }

            var dust = new GameObject("Zombie Dust");
            dust.transform.SetParent(parent, false);
            return dust.AddComponent<UnitDustEffect>();
        }


        // 현재 표시 자세의 먼지 출발 좌표 구성과 연출 시작
        internal void PlayFromVisual(UnitDeathVisuals deathVisuals, SpriteRenderer visual)
        {
            // 같은 몸체 규격의 출발 좌표 배열 재사용
            if (startPositions == null || startPositions.Length != deathVisuals.DustPoints.Length)
            {
                startPositions = new Vector3[deathVisuals.DustPoints.Length];
            }

            for (int i = 0; i < startPositions.Length; i++)
            {
                Vector2 point = deathVisuals.DustPoints[i];
                point.x = visual.flipX ? -point.x : point.x;
                startPositions[i] = visual.transform.TransformPoint(point);
            }

            Play(deathVisuals.PixelSprite, startPositions, deathVisuals.PixelSize, visual.sortingOrder);
        }


        // 시작 좌표마다 한 픽셀 먼지 생성
        public void Play(Sprite pixelSprite, Vector3[] startPositions, float pixelSize, int sortingOrder)
        {
            // 표시 자원이나 시작 좌표가 없으면 빈 연출 개체 제거
            if (pixelSprite == null || startPositions == null || startPositions.Length == 0 || pixelSize <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            // 같은 규격의 픽셀 배열과 렌더러 재사용 및 이전 재생 상태 초기화
            int count = startPositions.Length;
            this.pixelSize = pixelSize;
            elapsed = 0f;
            duration = 0f;

            if (pixels == null || pixels.Length != count)
            {
                if (pixels != null)
                {
                    foreach (SpriteRenderer pixel in pixels)
                    {
                        Destroy(pixel.gameObject);
                    }
                }

                pixels = new SpriteRenderer[count];
                origins = new Vector2[count];
                offsets = new Vector2[count];
                velocities = new Vector2[count];
                delays = new float[count];
                lifetimes = new float[count];
            }

            for (int i = 0; i < count; i++)
            {
                // 공용 한 픽셀 스프라이트로 개별 렌더러 생성
                var renderer = pixels[i];

                if (renderer == null)
                {
                    var pixel = new GameObject("Dust Pixel");
                    pixel.transform.SetParent(transform, false);
                    renderer = pixel.AddComponent<SpriteRenderer>();
                    pixels[i] = renderer;
                }

                renderer.transform.position = startPositions[i];
                renderer.enabled = true;
                renderer.color = Color.white;
                offsets[i] = Vector2.zero;
                renderer.sprite = pixelSprite;
                renderer.sortingOrder = sortingOrder;
                pixels[i] = renderer;

                // 무작위 지연과 속도로 동시에 흩어지지 않고 순차적으로 떠오르는 먼지 구성
                origins[i] = renderer.transform.localPosition;
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
            if (pixels == null || Time.deltaTime <= 0f)
            {
                return;
            }

            // 모든 픽셀에 동일한 프레임 감쇠값 재사용
            float driftDecay = Mathf.Exp(-DriftDamping * Time.deltaTime);
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
                velocities[i].x *= driftDecay;
                offsets[i] += velocities[i] * Time.deltaTime;

                // 시작점 기준 이동량을 픽셀 단위로 끊어 도트 그래픽 규격 유지
                Vector2 snapped = new Vector2(Mathf.Round(offsets[i].x / pixelSize), Mathf.Round(offsets[i].y / pixelSize)) * pixelSize;
                pixels[i].transform.localPosition = origins[i] + snapped;
                pixels[i].color = new Color(1f, 1f, 1f, 1f - progress);
            }

            if (elapsed >= duration)
            {
                // 지도 교체 시 함께 파괴될 부모 아래에서만 완료 연출 보관
                gameObject.SetActive(false);
                AvailableEffects.Add(this);
            }
        }


        // 지도와 연출 파괴 시 정적 대여 목록의 참조 정리
        private void OnDestroy()
        {
            // 종료된 지도의 연출 자원 보존 방지
            AvailableEffects.Remove(this);
        }
    }
}
