using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RotWorld
{
    /// <summary>
    /// 클릭한 인간의 허기와 갈증 표시 및 화면 경계 내 유닛 추적
    /// </summary>
    public sealed class HumanNeedsPanel
    {
        // 기존 지도와 화면 좌표 변환 참조
        private readonly CityMapView mapView;
        private readonly Camera worldCamera;
        private readonly RectTransform canvasRect;
        private readonly RectTransform panel;

        // 선택된 인간의 발밑 표시와 화면상 최소 크기
        private readonly RectTransform selectionMarker;
        private const float MinimumMarkerWidth = 12f;
        private const float MarkerWidthMultiplier = 1.6f;
        private const float MinimumMarkerHeight = 6f;
        private const float MarkerHeightRatio = .45f;
        private const float MarkerFootGap = 1f;

        // 개인별 생존 수치 표시와 선택 상태
        private readonly TMP_Text hungerLabel;
        private readonly TMP_Text thirstLabel;
        private UnitWander selectedHuman;
        private CityMap selectedMap;
        private double displayedHunger = -1d;
        private double displayedThirst = -1d;

        // UI 클릭 차단과 화면 경계 여백
        private readonly PointerEventData pointer;
        private readonly List<RaycastResult> raycasts = new List<RaycastResult>();
        private const float UnitGap = 12f;
        private const float ScreenMargin = 8f;

        // 기존 캔버스와 표시 요소를 선택 및 위치 추적에 연결
        public HumanNeedsPanel(CityMapView mapView, Camera worldCamera, RectTransform canvasRect,
            RectTransform panel, TMP_Text hungerLabel, TMP_Text thirstLabel, RectTransform selectionMarker)
        {
            // 필수 표시 참조와 입력 시스템 누락의 조기 확인
            if (mapView == null || worldCamera == null || canvasRect == null || panel == null ||
                hungerLabel == null || thirstLabel == null || selectionMarker == null || EventSystem.current == null)
            {
                throw new ArgumentException("인간 정보 UI의 지도, 카메라, 표시 요소 또는 이벤트 시스템 누락");
            }

            this.mapView = mapView;
            this.worldCamera = worldCamera;
            this.canvasRect = canvasRect;
            this.panel = panel;
            this.hungerLabel = hungerLabel;
            this.thirstLabel = thirstLabel;
            this.selectionMarker = selectionMarker;
            pointer = new PointerEventData(EventSystem.current);
        }


        // 일시정지와 무관한 인간 선택 및 현재 수치와 위치 갱신
        public void Refresh(bool canSelect)
        {
            // 소환 모드와 생성창에서는 선택 해제 및 정보 표시 중단
            if (!canSelect || mapView.IsInputBlocked)
            {
                selectedHuman = null;
                panel.gameObject.SetActive(false);
                selectionMarker.gameObject.SetActive(false);
                return;
            }

            // UI 클릭을 제외한 지도 클릭으로 인간 교체 또는 같은 인간과 빈 공간 선택 해제
            var mouse = Mouse.current;

            if (mouse != null && Application.isFocused && mouse.leftButton.wasPressedThisFrame)
            {
                pointer.position = mouse.position.ReadValue();
                raycasts.Clear();
                EventSystem.current.RaycastAll(pointer, raycasts);

                if (raycasts.Count == 0)
                {
                    var clickedHuman = mapView.GetHumanAtScreenPosition(pointer.position);
                    selectedHuman = clickedHuman == selectedHuman ? null : clickedHuman;
                    selectedMap = mapView.Map;
                    displayedHunger = displayedThirst = -1d;
                }
            }

            // 지도 교체와 쓰러짐 및 좀비 전환 후 이전 인간 정보 제거
            if (selectedHuman == null || selectedMap != mapView.Map || !selectedHuman.isActiveAndEnabled ||
                !selectedHuman.IsHumanTarget || selectedHuman.Needs == null)
            {
                selectedHuman = null;
                panel.gameObject.SetActive(false);
                selectionMarker.gameObject.SetActive(false);
                return;
            }

            RefreshPosition();
            RefreshValues();
        }


        // 계산값의 소수 부분을 제외한 정수만 텍스트에 전달
        private void RefreshValues()
        {
            // 원본 수치를 유지하고 표시값만 정수로 변환 후 중복 갱신 방지
            double hunger = Math.Floor(selectedHuman.Needs.Hunger);
            double thirst = Math.Floor(selectedHuman.Needs.Thirst);

            if (displayedHunger != hunger)
            {
                hungerLabel.SetText("허기  {0}", (float)hunger);
                displayedHunger = hunger;
            }

            if (displayedThirst != thirst)
            {
                thirstLabel.SetText("갈증  {0}", (float)thirst);
                displayedThirst = thirst;
            }
        }


        // 유닛 우측 하단 배치와 화면 우측 공간 부족 시 좌측 하단 전환
        private void RefreshPosition()
        {
            // 화면 밖 유닛은 선택을 유지하되 정보 표시만 숨김
            Rect body = UnitWalkAnimation.BodyBounds;
            Vector3 center = worldCamera.WorldToScreenPoint(selectedHuman.transform.TransformPoint(body.center));

            if (center.z <= 0f || !worldCamera.pixelRect.Contains(center))
            {
                panel.gameObject.SetActive(false);
                selectionMarker.gameObject.SetActive(false);
                return;
            }

            // 유닛 몸체 양 끝을 캔버스 좌표로 변환해 줌과 해상도 변경 반영
            Vector2 screenMin = worldCamera.WorldToScreenPoint(selectedHuman.transform.TransformPoint(body.min));
            Vector2 screenMax = worldCamera.WorldToScreenPoint(selectedHuman.transform.TransformPoint(body.max));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenMin, null, out Vector2 min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenMax, null, out Vector2 max);
            Vector2 size = panel.rect.size;
            Rect bounds = canvasRect.rect;

            // 기존 화면상 크기를 월드 크기로 변환해 유닛 아래에 선택 표시 배치
            float markerWidth = Mathf.Max(MinimumMarkerWidth, (max.x - min.x) * MarkerWidthMultiplier);
            selectionMarker.sizeDelta = new Vector2(markerWidth, Mathf.Max(MinimumMarkerHeight, markerWidth * MarkerHeightRatio));
            float worldUnitsPerCanvasUnit = worldCamera.orthographicSize * 2f / Mathf.Max(1, worldCamera.pixelHeight) * canvasRect.lossyScale.y;
            Vector3 parentScale = selectionMarker.parent.lossyScale;
            selectionMarker.localScale = new Vector3(worldUnitsPerCanvasUnit / parentScale.x,
                worldUnitsPerCanvasUnit / parentScale.y, 1f);
            Vector3 footPosition = selectedHuman.transform.TransformPoint(new Vector3(body.center.x, body.yMin, 0f));
            selectionMarker.position = footPosition - Vector3.up * (MarkerFootGap * worldUnitsPerCanvasUnit);
            selectionMarker.gameObject.SetActive(true);

            // 우측 여유가 줄어드는 구간에서 화면상 위치에 비례한 좌측 배치 전환
            float rightX = max.x + UnitGap;
            float leftX = min.x - UnitGap - size.x;
            float rightEdge = bounds.xMax - ScreenMargin;
            float transition = Mathf.InverseLerp(rightEdge - size.x - UnitGap, rightEdge, max.x);
            float x = Mathf.Lerp(rightX, leftX, transition);

            // 하단과 좌측 경계까지 보정해 패널 전체를 화면 안에 유지
            float maxX = Mathf.Max(bounds.xMin + ScreenMargin, bounds.xMax - ScreenMargin - size.x);
            float minY = Mathf.Min(bounds.yMax - ScreenMargin, bounds.yMin + ScreenMargin + size.y);
            x = Mathf.Clamp(x, bounds.xMin + ScreenMargin, maxX);
            float y = Mathf.Clamp(min.y - UnitGap, minY, bounds.yMax - ScreenMargin);

            // 시간 지연 없이 현재 유닛과 화면 위치에 맞춘 즉시 배치
            panel.localPosition = new Vector3(x, y, 0f);
            panel.gameObject.SetActive(true);
        }
    }
}
