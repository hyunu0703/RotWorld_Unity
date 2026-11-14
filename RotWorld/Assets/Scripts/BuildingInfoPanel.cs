using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RotWorld
{
    /// <summary>
    /// 클릭한 건물 이름 표시와 지붕 위 화면 위치 추적
    /// </summary>
    public sealed class BuildingInfoPanel
    {
        // 지도와 화면 캔버스 및 표시 요소 연결
        private readonly CityMapView mapView;
        private readonly Camera worldCamera;
        private readonly RectTransform canvasRect;
        private readonly RectTransform panel;
        private readonly TMP_Text nameLabel;

        // 텍스트 밀착 여백과 지도 축소 단계별 표시 크기
        private const float FontSize = 16f;
        private const float HorizontalPadding = 6f;
        private const float VerticalPadding = 3f;
        private const float BuildingGap = 4f;
        private const float MinimumZoomScale = .25f;
        private const float MaximumZoomScale = 1.1f;

        // 선택 건물과 지도 교체 감지
        private CityBuilding selectedBuilding;
        private CityMap selectedMap;

        // 기존 UI 클릭의 지도 선택 전파 차단
        private readonly PointerEventData pointer;
        private readonly List<RaycastResult> raycasts = new List<RaycastResult>();


        // 기존 지도와 UI 요소를 건물 선택 표시에 연결
        public BuildingInfoPanel(CityMapView mapView, Camera worldCamera, RectTransform canvasRect,
            RectTransform panel, TMP_Text nameLabel)
        {
            // 최초 갱신 전에 필수 참조와 입력 시스템 확인
            if (mapView == null || worldCamera == null || canvasRect == null || panel == null ||
                nameLabel == null || EventSystem.current == null)
            {
                throw new ArgumentException("건물 정보 UI의 지도, 카메라, 표시 요소 또는 이벤트 시스템 누락");
            }

            this.mapView = mapView;
            this.worldCamera = worldCamera;
            this.canvasRect = canvasRect;
            this.panel = panel;
            this.nameLabel = nameLabel;
            pointer = new PointerEventData(EventSystem.current);

            // 고정 폭과 줄바꿈을 제거하고 텍스트 주위에 작은 여백만 확보
            nameLabel.fontSize = FontSize;
            nameLabel.enableAutoSizing = false;
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            var textRect = nameLabel.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(HorizontalPadding, VerticalPadding);
            textRect.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);
        }


        // 일시정지와 무관한 건물 선택 및 카메라 변경 추적
        public void Refresh(bool canSelect)
        {
            // 소환 모드와 생성창 진입 및 지도 교체 후 오래된 선택 제거
            if (!canSelect || mapView.IsInputBlocked || selectedMap != mapView.Map)
            {
                selectedBuilding = null;
                selectedMap = mapView.Map;
                panel.gameObject.SetActive(false);
            }

            if (!canSelect || mapView.IsInputBlocked)
            {
                return;
            }

            // UI 외부 왼쪽 클릭만 처리하며 인물 선택을 우선
            var mouse = Mouse.current;

            if (mouse != null && Application.isFocused && mouse.leftButton.wasPressedThisFrame)
            {
                pointer.position = mouse.position.ReadValue();
                raycasts.Clear();
                EventSystem.current.RaycastAll(pointer, raycasts);

                if (raycasts.Count == 0)
                {
                    var clicked = mapView.GetHumanAtScreenPosition(pointer.position) == null
                        ? mapView.GetBuildingAtScreenPosition(pointer.position) : null;
                    selectedBuilding = clicked == selectedBuilding ? null : clicked;

                    if (selectedBuilding != null)
                    {
                        RefreshName();
                    }
                }
            }

            if (selectedBuilding == null)
            {
                panel.gameObject.SetActive(false);
                return;
            }

            RefreshPosition();
        }


        // 선택한 건물 이름의 실제 글자 크기에 맞춰 패널 크기 갱신
        private void RefreshName()
        {
            // 선택 변경 시에만 폰트 기준 권장 크기 계산과 좌우 및 상하 여백 추가
            nameLabel.text = selectedBuilding.Name;
            Vector2 textSize = nameLabel.GetPreferredValues(selectedBuilding.Name);
            panel.sizeDelta = new Vector2(Mathf.Ceil(textSize.x) + HorizontalPadding * 2f,
                Mathf.Ceil(textSize.y) + VerticalPadding * 2f);
        }


        // 건물 상단 중앙을 화면 좌표로 변환해 이름 패널 배치
        private void RefreshPosition()
        {
            // 외형의 상단 경계에 맞춰 지도 부모 변환과 카메라 줌 반영
            var bounds = selectedBuilding.Bounds;
            var top = new Vector3(bounds.center.x, bounds.yMax, 0f);
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(mapView.transform.TransformPoint(top));

            if (screenPosition.z <= 0f || !worldCamera.pixelRect.Contains(screenPosition))
            {
                panel.gameObject.SetActive(false);
                return;
            }

            // 지도 줌 단계에 따라 패널과 자식 텍스트 및 건물 위 여백을 함께 축소
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 local);
            float scale = Mathf.Lerp(MinimumZoomScale, MaximumZoomScale, mapView.ZoomProgress);
            panel.localScale = Vector3.one * scale;
            panel.anchoredPosition = local + Vector2.up * (BuildingGap * scale);
            panel.gameObject.SetActive(true);
        }
    }
}
