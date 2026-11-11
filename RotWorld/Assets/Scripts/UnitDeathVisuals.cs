using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 좀비 사망 연출이 공유하는 흰색 전환 프레임과 먼지 픽셀 자원 보관
    /// </summary>
    public sealed class UnitDeathVisuals
    {
        // CityMapView가 생성 및 해제하는 공용 표시 자원
        public Sprite[] FadeFrames { get; }
        public Sprite PixelSprite { get; }

        // 스프라이트 원점 기준 몸체 픽셀 중심 좌표와 한 픽셀의 월드 크기
        public Vector2[] DustPoints { get; }
        public float PixelSize { get; }

        // 공용 자원 묶음 생성
        public UnitDeathVisuals(Sprite[] fadeFrames, Sprite pixelSprite, Vector2[] dustPoints, float pixelSize)
        {
            // 누락된 목록은 빈 배열로 보관해 사용처의 길이 검사 단순화
            FadeFrames = fadeFrames ?? new Sprite[0];
            PixelSprite = pixelSprite;
            DustPoints = dustPoints ?? new Vector2[0];
            PixelSize = pixelSize;
        }
    }
}
