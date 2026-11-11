using System;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace RotWorld.Editor
{
    /// <summary>
    /// 프로젝트 폰트의 한글 누락과 TMP 메시 생성 회귀 검사
    /// </summary>
    public static class KoreanFontValidation
    {
        // 실제 UI에서 사용하는 폰트와 한글 검사 범위
        private static readonly string[] FontPaths =
        {
            "Assets/Fonts/neodgm SDF.asset",
            "Assets/Fonts/neodgm_code SDF.asset"
        };
        private const int FirstSyllable = 0xAC00;
        private const int LastSyllable = 0xD7A3;
        private const int FirstCompatibilityJamo = 0x3131;
        private const int LastCompatibilityJamo = 0x3163;
        private const int FirstLeadingJamo = 0x1100;
        private const int LastLeadingJamo = 0x1112;
        private const int FirstVowelJamo = 0x1161;
        private const int LastVowelJamo = 0x1175;
        private const int FirstTrailingJamo = 0x11A8;
        private const int LastTrailingJamo = 0x11C2;
        private const int MeshBatchSize = 128;

        // 에디터 메뉴에서 전체 한글과 아틀라스 확장 검사
        [MenuItem("Tools/RotWorld/Validate Korean Fonts")]
        public static void Validate()
        {
            // 완성형 전체와 단독 입력 자모를 두 UI 폰트에 동일 적용
            var characters = new StringBuilder();
            AppendRange(characters, FirstSyllable, LastSyllable);
            AppendRange(characters, FirstCompatibilityJamo, LastCompatibilityJamo);
            AppendRange(characters, FirstLeadingJamo, LastLeadingJamo);
            AppendRange(characters, FirstVowelJamo, LastVowelJamo);
            AppendRange(characters, FirstTrailingJamo, LastTrailingJamo);

            foreach (string path in FontPaths)
            {
                ValidateFont(path, characters.ToString());
            }

            Debug.Log($"Korean font validation PASS: {characters.Length} characters per font");
        }


        // 명령행 검증 결과를 프로세스 종료 코드로 전달
        public static void RunBatch()
        {
            // 검사 실패의 예외와 종료 코드를 CI 또는 로컬 호출자에 전달
            try
            {
                Validate();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }


        // 검사할 연속 유니코드 범위를 문자열에 추가
        private static void AppendRange(StringBuilder characters, int first, int last)
        {
            // 완성형 또는 호환 자모의 양 끝을 포함한 검사 목록 구성
            for (int codePoint = first; codePoint <= last; codePoint++)
            {
                characters.Append((char)codePoint);
            }
        }


        // 에셋 설정을 복제한 임시 폰트로 누적 글리프와 메시 검사
        private static void ValidateFont(string path, string characters)
        {
            // 원본 에셋 변경 없이 빌드 직후의 빈 동적 아틀라스 조건 재현
            var configuredFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

            if (configuredFont == null || configuredFont.sourceFontFile == null)
            {
                throw new InvalidOperationException($"Missing source font: {path}");
            }

            var font = TMP_FontAsset.CreateFontAsset(configuredFont.sourceFontFile,
                Mathf.RoundToInt(configuredFont.faceInfo.pointSize), configuredFont.atlasPadding,
                configuredFont.atlasRenderMode, configuredFont.atlasWidth, configuredFont.atlasHeight,
                configuredFont.atlasPopulationMode, configuredFont.isMultiAtlasTexturesEnabled);
            var canvasObject = new GameObject("Korean font validation", typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var textObject = new GameObject("Korean font text", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);

            try
            {
                // 단일 페이지를 넘는 한글을 누적한 뒤 실제 문제 문자열을 마지막에 추가
                if (!font.TryAddCharacters(characters.Replace("초", ""), out string missing))
                {
                    throw new InvalidOperationException($"{path}: missing {missing.Length} characters, first U+{(int)missing[0]:X4}");
                }

                if (!font.HasCharacters("초대형 1024", out uint[] missingLabel, false, true))
                {
                    throw new InvalidOperationException($"{path}: 초대형 1024 missing {missingLabel.Length} characters");
                }

                if (font.atlasTextureCount < 2)
                {
                    throw new InvalidOperationException($"{path}: atlas overflow was not exercised");
                }

                // 각 글자의 원래 유니코드와 가시 메시 및 아틀라스 참조 검사
                var text = textObject.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = 16;
                text.rectTransform.sizeDelta = new Vector2(20000, 100);
                ValidateMesh(text, "초대형 1024");

                for (int offset = 0; offset < characters.Length; offset += MeshBatchSize)
                {
                    ValidateMesh(text, characters.Substring(offset, Math.Min(MeshBatchSize, characters.Length - offset)));
                }

                Debug.Log($"{path}: PASS {characters.Length} Hangul characters, {font.atlasTextureCount} atlas pages, 초대형 1024 mesh");
            }
            finally
            {
                // 검사 전용 오브젝트와 동적 아틀라스 메모리 해제
                UnityEngine.Object.DestroyImmediate(canvasObject);

                foreach (Texture2D texture in font.atlasTextures)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                UnityEngine.Object.DestroyImmediate(font.material);
                UnityEngine.Object.DestroyImmediate(font);
            }
        }


        // 대체 문자나 비어 있는 한글 메시 발생 여부 검사
        private static void ValidateMesh(TMP_Text text, string characters)
        {
            // TMP의 실제 문자별 메시를 생성한 후 입력 유니코드와 비교
            text.text = characters;
            text.ForceMeshUpdate(true, true);

            if (text.textInfo.characterCount != characters.Length)
            {
                throw new InvalidOperationException($"TMP character count mismatch: expected {characters.Length}, actual {text.textInfo.characterCount}");
            }

            for (int index = 0; index < characters.Length; index++)
            {
                TMP_CharacterInfo info = text.textInfo.characterInfo[index];

                if (characters[index] != ' ' &&
                    (info.textElement == null || info.textElement.unicode != characters[index] || !info.isVisible ||
                    info.textElement.glyph.glyphRect.width == 0 || info.textElement.glyph.glyphRect.height == 0 ||
                    info.materialReferenceIndex >= text.textInfo.materialCount))
                {
                    throw new InvalidOperationException($"TMP mesh missing U+{(int)characters[index]:X4}");
                }
            }
        }
    }
}
