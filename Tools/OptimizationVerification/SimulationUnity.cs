using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    /// <summary>
    /// 테스트 프레임 끝의 개체 제거 대기와 콜백 호출
    /// </summary>
    public class Object
    {
        // 프레임 종료 시 제거할 개체 목록
        private static readonly List<GameObject> Pending = new List<GameObject>();

        // Unity의 프레임 끝 파괴 요청 대역
        public static void Destroy(Object value)
        {
            // 동일 개체의 중복 제거 요청 제외
            if (value is GameObject actor && !actor.destroyed && !Pending.Contains(actor))
            {
                Pending.Add(actor);
            }
        }


        // 테스트 프레임의 대기 개체 제거
        public static void Flush()
        {
            // 콜백 중 목록 변경에 대비한 스냅샷 순회
            foreach (GameObject actor in Pending.ToArray())
            {
                actor.Dispose();
            }

            Pending.Clear();
        }
    }

    /// <summary>
    /// 실제 계산 코드에 필요한 컴포넌트 소유 관계 대역
    /// </summary>
    public class Component : Object
    {
        // 테스트 개체와 Transform 조회 계약
        public GameObject gameObject;
        public Transform transform => gameObject.transform;

        // 소유 개체의 컴포넌트 조회 대역
        public bool TryGetComponent<T>(out T component) where T : Component
        {
            // 테스트 개체의 등록된 타입 조회
            component = gameObject.GetComponent<T>();
            return component != null;
        }
    }

    /// <summary>
    /// 프레임 처리 대상의 활성 상태 대역
    /// </summary>
    public class MonoBehaviour : Component
    {
        // Unity 활성 상태 조회 계약
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && !gameObject.destroyed;
    }

    /// <summary>
    /// GPU 표시를 제외한 스프라이트 참조 대역
    /// </summary>
    public class Sprite : Object
    {
    }

    /// <summary>
    /// 계산에 필요한 컴포넌트 생성과 종료 콜백 대역
    /// </summary>
    public class GameObject : Object
    {
        // 테스트 소유 개체와 등록 컴포넌트
        public string name;
        public bool destroyed;
        public readonly Transform transform = new Transform();
        private readonly List<Component> components = new List<Component>();

        // 테스트 개체 이름 초기화
        public GameObject(string name)
        {
            // 게임 코드의 표시 이름 저장 계약 유지
            this.name = name;
        }


        // 실제 게임 컴포넌트의 관리 인스턴스 생성
        public T AddComponent<T>() where T : Component, new()
        {
            // 네이티브 Unity 초기화 없이 소유 개체 연결
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }


        // 등록 타입의 첫 컴포넌트 반환
        public T GetComponent<T>() where T : Component
        {
            // 게임 코드의 타입별 컴포넌트 접근 지원
            return components.OfType<T>().FirstOrDefault();
        }


        // 비활성화와 파괴 콜백을 동일 순서로 실행
        public void Dispose()
        {
            // 이미 제거된 개체의 중복 콜백 차단
            if (destroyed)
            {
                return;
            }

            foreach (Component component in components)
            {
                component.GetType().GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
            }

            foreach (Component component in components)
            {
                component.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
            }

            destroyed = true;
        }
    }

    /// <summary>
    /// 논리 이동 결과를 보관하는 위치 대역
    /// </summary>
    public class Transform
    {
        // 게임 코드의 최종 표시 위치 저장
        public Vector3 localPosition;
    }

    /// <summary>
    /// 결정적 프레임 입력 대역
    /// </summary>
    public static class Time
    {
        // 게임 계산에서 사용하는 동일 프레임 시간
        public static float deltaTime;
        public static float time;
        public static int frameCount;
    }

    /// <summary>
    /// 필수 참조 오류를 테스트 실패로 전달
    /// </summary>
    public static class Debug
    {
        // 게임 초기화 오류의 테스트 중단
        public static void LogError(string message, Object context)
        {
            // 숨겨진 초기화 실패 대신 명확한 오류 반환
            throw new Exception(message);
        }
    }
}

namespace RotWorld
{
    /// <summary>
    /// 몸체 경계만 제공하고 GPU 표시를 제외하는 테스트 대역
    /// </summary>
    public class UnitWalkAnimation : MonoBehaviour
    {
        // 실제 게임과 동일한 몸체 경계
        internal static readonly Rect BodyBounds = new Rect(.25f, 0f, .375f, .875f);

        // GPU 표시 제외를 위한 SetMap 호출 계약 대역
        public void SetMap(CityMap map)
        {
        }


        // GPU 표시 제외를 위한 SetUnitType 호출 계약 대역
        public void SetUnitType(bool zombie)
        {
        }


        // GPU 표시 제외를 위한 Stop 호출 계약 대역
        public void Stop(bool resetStride = true)
        {
        }


        // GPU 표시 제외를 위한 Walk 호출 계약 대역
        public void Walk(Vector2 direction, float travelled)
        {
        }


        // GPU 표시 제외를 위한 SetFallProgress 호출 계약 대역
        public void SetFallProgress(float value)
        {
        }


        // GPU 표시 제외를 위한 SetDeathProgress 호출 계약 대역
        public void SetDeathProgress(Sprite[] frames, float value)
        {
        }


        // GPU 표시 제외를 위한 SetConversionProgress 호출 계약 대역
        public void SetConversionProgress(Sprite[] frames, float value)
        {
        }


        // GPU 표시 제외를 위한 SetKnockbackHeight 호출 계약 대역
        public void SetKnockbackHeight(float value)
        {
        }


        // GPU 표시 제외를 위한 SetDownedSortingOrder 호출 계약 대역
        public void SetDownedSortingOrder()
        {
        }


        // GPU 표시 제외를 위한 SetHitColor 호출 계약 대역
        public void SetHitColor(bool value)
        {
        }


        // GPU 표시 제외를 위한 SetAttackOffset 호출 계약 대역
        public void SetAttackOffset(Vector2 value)
        {
        }


        // GPU 표시 제외를 위한 ChangeToZombie 호출 계약 대역
        public void ChangeToZombie(Sprite[] frames)
        {
        }


        // GPU 표시 제외를 위한 Disintegrate 호출 계약 대역
        public void Disintegrate(UnitDeathVisuals visuals)
        {
        }
    }
}

namespace Baseline
{
    /// <summary>
    /// 몸체 경계만 제공하고 GPU 표시를 제외하는 테스트 대역
    /// </summary>
    public class UnitWalkAnimation : MonoBehaviour
    {
        // 실제 게임과 동일한 몸체 경계
        internal static readonly Rect BodyBounds = new Rect(.25f, 0f, .375f, .875f);

        // GPU 표시 제외를 위한 SetMap 호출 계약 대역
        public void SetMap(CityMap map)
        {
        }


        // GPU 표시 제외를 위한 SetUnitType 호출 계약 대역
        public void SetUnitType(bool zombie)
        {
        }


        // GPU 표시 제외를 위한 Stop 호출 계약 대역
        public void Stop(bool resetStride = true)
        {
        }


        // GPU 표시 제외를 위한 Walk 호출 계약 대역
        public void Walk(Vector2 direction, float travelled)
        {
        }


        // GPU 표시 제외를 위한 SetFallProgress 호출 계약 대역
        public void SetFallProgress(float value)
        {
        }


        // GPU 표시 제외를 위한 SetDeathProgress 호출 계약 대역
        public void SetDeathProgress(Sprite[] frames, float value)
        {
        }


        // GPU 표시 제외를 위한 SetConversionProgress 호출 계약 대역
        public void SetConversionProgress(Sprite[] frames, float value)
        {
        }


        // GPU 표시 제외를 위한 SetKnockbackHeight 호출 계약 대역
        public void SetKnockbackHeight(float value)
        {
        }


        // GPU 표시 제외를 위한 SetDownedSortingOrder 호출 계약 대역
        public void SetDownedSortingOrder()
        {
        }


        // GPU 표시 제외를 위한 SetHitColor 호출 계약 대역
        public void SetHitColor(bool value)
        {
        }


        // GPU 표시 제외를 위한 SetAttackOffset 호출 계약 대역
        public void SetAttackOffset(Vector2 value)
        {
        }


        // GPU 표시 제외를 위한 ChangeToZombie 호출 계약 대역
        public void ChangeToZombie(Sprite[] frames)
        {
        }


        // GPU 표시 제외를 위한 Disintegrate 호출 계약 대역
        public void Disintegrate(UnitDeathVisuals visuals)
        {
        }
    }
}
