// 변경 전 커밋과 현재 소스의 순수 계산 결과 및 할당량 비교
const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const root = path.resolve(__dirname, '../..');
const baseline = 'f8e977d457b282adf3aa0f6177426f3124746e6f';
const work = fs.mkdtempSync(path.join(root, 'RotWorld/Temp/optimization-verification-'));
const scripts = path.join(root, 'RotWorld/Assets/Scripts');
const unity = process.env.UNITY_EDITOR_DIR || 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor';
const files = ['CityGenerator', 'CityLayout', 'UnitPathfinder', 'UnitSight', 'HumanNeeds'];
let program = fs.readFileSync(path.join(__dirname, 'Verification.cs'), 'utf8');
let includes = '';

for (const name of [...files, 'UnitWander', 'CityMapView']) {
    const source = cp.execFileSync('git', ['show', baseline + ':RotWorld/Assets/Scripts/' + name + '.cs'], { cwd: root, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 });
    fs.writeFileSync(path.join(work, name + '.baseline.cs'), source.replace('namespace RotWorld', 'namespace Baseline'));
}

for (const name of files) {
    includes += '<Compile Include="' + path.join(scripts, name + '.cs') + '"/><Compile Include="' + name + '.baseline.cs"/>';
}

for (const ns of ['RotWorld', 'Baseline']) {
    const source = fs.readFileSync(ns === 'RotWorld' ? path.join(scripts, 'UnitWander.cs') : path.join(work, 'UnitWander.baseline.cs'), 'utf8');
    const start = source.indexOf('        private bool HasUnitClearance');
    const actionStart = source.indexOf('        // 실제 위치와 행동 검색 격자의 동시 변경', start);
    const end = actionStart >= 0 ? actionStart : source.indexOf('        // 감지와 추적에서 참조할 실제 좌표', start);
    const conversion = source.indexOf('        private bool HasConversionClearance');
    const conversionEnd = source.indexOf('        // 포물선 피격', conversion);
    // Unity 콜백과 렌더러를 제외한 실제 충돌 함수의 독립 검증
    program += '\nnamespace ' + ns + ' { public class UnitWander { private CityMap map; private UnityEngine.Vector2 position; private bool isZombie; private int populationIndex; private const float SeparationDistance=.9f; private readonly System.Collections.Generic.List<int> clearanceCandidates=new System.Collections.Generic.List<int>(); ' + source.slice(start, end) + source.slice(conversion, conversionEnd) + actionMembers(source, ns) + ' } }';
    const view = fs.readFileSync(ns === 'RotWorld' ? path.join(scripts, 'CityMapView.cs') : path.join(work, 'CityMapView.baseline.cs'), 'utf8');
    let pixels;
    let helpers;

    if (view.includes('private static void PaintMap(')) {
        pixels = '            int width = map.Size * PixelsPerTile;\n            var pixels = new Color32[width * width];\n' + methodBody(view, 'private static void PaintMap(');
        helpers = ['private static Color32 Hex(', 'private static void Fill(NativeArray<Color32>', 'private static void DrawBuilding(']
            .map(signature => method(view, signature).replaceAll('NativeArray<Color32>', 'Color32[]')).join('\n');
    } else {
        const pixelStart = view.indexOf('            int width = map.Size * PixelsPerTile;');
        const pixelEnd = view.indexOf('            // 완성된 픽셀 배열', pixelStart);
        pixels = view.slice(pixelStart, pixelEnd);
        helpers = view.slice(view.indexOf('        private static Color32 Hex'), view.indexOf('        // 인도 위 작은 인물'));
    }

    program += '\nnamespace ' + ns + ' { internal static class PixelBuilder { private const int PixelsPerTile=8; private const int HorizontalHashMultiplier=73856093; private const int VerticalHashMultiplier=19349663; private const int GrassVariationInterval=19; private const int SandVariationInterval=23; internal static Color32[] Build(CityMap map) { ' + pixels + ' return pixels; } ' + helpers + ' } }';
}

for (const ns of ['RotWorld', 'Baseline']) {
    const text = ns === 'RotWorld' ? fs.readFileSync(path.join(scripts, 'UnitCombat.cs'), 'utf8') : cp.execFileSync('git', ['show', baseline + ':RotWorld/Assets/Scripts/UnitCombat.cs'], { cwd: root, encoding: 'utf8' });
    const start = text.indexOf(ns === 'RotWorld' ? '            map.ZombieQueries.Collect' : '            foreach (var zombie in map.ZombieUnits.Values)');
    const end = text.indexOf('            if (target == null)', start);
    program += '\nnamespace ' + ns + ' { internal static class UnitWalkAnimation {internal static UnityEngine.Rect BodyBounds => new UnityEngine.Rect(.25f,0f,.375f,.875f);} internal static class CombatSelection { internal static UnitWander Find(CityMap map, UnityEngine.Vector2 position) { UnitWander target=null; UnitWander requiredTarget=null; float bestDistance=1.101f; ' + (ns === 'RotWorld' ? 'const float DistanceTolerance=.001f; ' : '') + ' var attackCandidates=new System.Collections.Generic.List<int>(); ' + text.slice(start, end) + ' return target; } } }';
}

fs.writeFileSync(path.join(work, 'Program.cs'), program);
fs.writeFileSync(path.join(work, 'Test.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '<Compile Include="' + path.join(scripts, 'UnitSpatialIndex.cs') + '"/><Compile Include="' + path.join(scripts, 'UnitQueryIndex.cs') + '"/><Compile Include="' + path.join(scripts, 'GridChangeTracker.cs') + '"/><Compile Include="Program.cs"/><Reference Include="UnityEngine.CoreModule"><HintPath>' + path.join(unity, 'Data/Managed/UnityEngine/UnityEngine.CoreModule.dll') + '</HintPath></Reference></ItemGroup></Project>');
console.log('Verification directory: ' + work);
const result = cp.spawnSync('dotnet', ['run', '--project', path.join(work, 'Test.csproj'), '-c', 'Release', '-v', 'quiet'], { cwd: work, stdio: 'inherit' });
if (fs.existsSync(path.join(work, 'verification.txt'))) console.log(fs.readFileSync(path.join(work, 'verification.txt'), 'utf8'));
process.exitCode = result.status ?? 1;

function method(source, signature) {
    const start = source.indexOf(signature);
    if (start < 0) throw new Error('Missing method: ' + signature);
    const brace = source.indexOf('{', start);
    let depth = 1, end = brace + 1;
    while (depth && end < source.length) { if (source[end] === '{') depth++; if (source[end] === '}') depth--; end++; }
    return source.slice(start, end);
}
function methodBody(source, signature) { const text = method(source, signature); return text.slice(text.indexOf('{') + 1, -1); }

function actionMembers(source, ns) {
    const common = ' private bool isDowned; private int chaseTarget = -1; private const float CooperativeDangerRadius = 5f; private const float ActionQueryMargin = .001f; private readonly System.Collections.Generic.List<int> threatCandidates = new System.Collections.Generic.List<int>(); private readonly System.Collections.Generic.List<int> pursuerCandidates = new System.Collections.Generic.List<int>(); public bool isActiveAndEnabled = true; public bool IsHumanTarget => !isZombie && !isDowned; public bool IsZombieTarget => isZombie && !isDowned; public UnityEngine.Vector2 Position => position; public int PopulationIndex => populationIndex; ';
    const threat = method(source, 'private bool HasOtherZombieThreat(');
    const setters = ns === 'RotWorld' ? method(source, 'private void SetPosition(') + method(source, 'private void SetChaseTarget(') : 'private void SetPosition(UnityEngine.Vector2 next){position=next;} private void SetChaseTarget(int next){chaseTarget=next;}';
    const helper = method(source, 'private UnitWander FindAvailableCooperativeHelper(');
    const helperState = 'private const float CooperativeRecruitRadius=6f; private const float KnockbackDuration=.28f; private float knockbackElapsed=.28f; private UnitWander cooperativeLeader; private UnitWander cooperativeHelper; public bool IsGrabbed; private readonly System.Collections.Generic.List<int> cooperativeCandidates=new System.Collections.Generic.List<int>();';
    const terrain = method(source, 'private bool IsTerrainClear(').replace('int samples =', 'TerrainChecks++; int samples =');
    const conversionBody = methodBody(source, 'private void TryConvertToZombie(');
    const begin = ns === 'RotWorld' ? conversionBody.indexOf('            // 이전 실패 뒤') : conversionBody.indexOf('            Vector2? destination');
    const finish = conversionBody.indexOf('            int oldIndex');
    const conversion = 'public UnityEngine.Vector2? TryConversion(){' + conversionBody.slice(begin, finish).replaceAll('return;', 'return null;') + ' return destination; }';
    const conversionState = (ns === 'RotWorld' ? 'private readonly GridChangeTracker.Watch failedConversionWatch=new GridChangeTracker.Watch();private UnityEngine.Vector2 failedConversionPosition;' : '') + 'private const float MaximumSubstep=.1f;public int TerrainChecks;';
    return common + threat + setters + helperState + helper + terrain + conversionState + conversion;
}
