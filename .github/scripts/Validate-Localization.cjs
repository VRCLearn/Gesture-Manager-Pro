const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '../..');
const directory = path.join(root, 'Scripts/Editor/Localization');
let checks = 0;
function check(value, message) {
    assert.ok(value, message);
    checks++;
}
function entries(file) {
    const source = fs.readFileSync(path.join(directory, file + '.cs'), 'utf8');
    const result = new Map();
    for (const match of source.matchAll(/\[("(?:\\.|[^"\\])*")\]\s*=\s*("(?:\\.|[^"\\])*")/g)) {
        const key = JSON.parse(match[1]);
        check(!result.has(key), 'Duplicate key in ' + file + ': ' + key);
        result.set(key, JSON.parse(match[2]));
    }
    check(result.size > 0, 'Empty catalog: ' + file);
    return result;
}
const simplified = new Map();
for (const file of ['GestureManagerChinese', 'LipSyncChinese', 'ModelStatisticsChinese', 'GameSimulationChinese'])
    for (const [key, value] of entries(file)) {
        check(!simplified.has(key) || simplified.get(key) === value, 'Conflicting Simplified Chinese key: ' + key);
        simplified.set(key, value);
    }
const catalogs = new Map([
    ['zh-Hans', simplified],
    ['zh-Hant', entries('TraditionalChineseTranslations')],
    ['ja', entries('JapaneseTranslations')],
    ['ko', entries('KoreanTranslations')],
]);
const english = entries('EnglishTranslations');
for (const key of english.keys()) check(simplified.has(key), 'Unknown English display key: ' + key);
catalogs.set('en', new Map([...simplified.keys()].map(key => [key, english.get(key) ?? key])));

const tokens = text => [...text.matchAll(/\{\d+(?:,-?\d+)?(?::[^{}]+)?\}/g)].map(match => match[0]).sort();
const tags = text => [...text.matchAll(/<\/?(?:b|i|color|size)(?:=[^>]*)?>/g)].map(match => match[0]).sort();
for (const [language, catalog] of catalogs) {
    check(catalog.size === simplified.size, language + ': key count differs');
    for (const [key, value] of catalog) {
        check(simplified.has(key), language + ': unknown key ' + key);
        check(typeof value === 'string' && value.trim().length > 0, language + ': empty translation ' + key);
        check(JSON.stringify(tokens(key)) === JSON.stringify(tokens(value)), language + ': changed format placeholders ' + key);
        check(JSON.stringify(tags(key)) === JSON.stringify(tags(value)), language + ': changed rich-text tags ' + key);
        check((key.match(/\n/g) ?? []).length === (value.match(/\n/g) ?? []).length, language + ': changed line breaks ' + key);
    }
}

// Literal calls must have explicit catalog coverage; identifiers and user data
// passed through dynamic call sites remain outside this display-string catalog.
function sources(folder) {
    return fs.readdirSync(folder, { withFileTypes: true }).flatMap(item => {
        const file = path.join(folder, item.name);
        return item.isDirectory() ? sources(file) : file.endsWith('.cs') ? [file] : [];
    });
}
for (const file of sources(path.join(root, 'Scripts'))) {
    if (file.startsWith(directory + path.sep)) continue;
    const source = fs.readFileSync(file, 'utf8');
    for (const match of source.matchAll(/EditorLocalization\.(?:Tr|Format)\(\s*("(?:\\.|[^"\\])*")/g))
        check(simplified.has(JSON.parse(match[1])), 'Uncatalogued display string in ' + path.relative(root, file) + ': ' + match[1]);
}
// Keep the agreed VRCX-0 performance terminology tied to an upstream snapshot,
// independent of our catalogs and later general translation reviews.
const performanceReference = JSON.parse(fs.readFileSync(path.join(__dirname, 'VrcxPerformanceReference.json'), 'utf8'));
check(performanceReference.source.repository === 'Map1en/VRCX-0', 'Unexpected performance terminology source');
check(/^[a-f0-9]{40}$/.test(performanceReference.source.revision), 'Performance reference must pin an upstream commit');
check(performanceReference.source.namespace === 'dialog.avatar.performance', 'Unexpected upstream performance namespace');
const performanceMapping = Object.entries(performanceReference.mapping);
check(performanceMapping.length === 38, 'Performance reference must cover 31 metrics, five ranks, Rating and legacy Bounds Size');
const upstreamKeys = new Set(performanceMapping.map(([, key]) => key));
check(Object.keys(performanceReference.languages).length === catalogs.size, 'Performance reference must cover all supported languages');
for (const [language, locale] of Object.entries(performanceReference.languages)) {
    check(catalogs.has(language), 'Unknown performance reference language: ' + language);
    const expected = performanceReference.translations[locale];
    check(expected && Object.keys(expected).length === upstreamKeys.size, locale + ': incomplete upstream performance reference');
    for (const [key, upstreamKey] of performanceMapping) {
        check(simplified.has(key), 'Unknown performance display key: ' + key);
        check(typeof expected[upstreamKey] === 'string' && expected[upstreamKey].length > 0,
            locale + ': missing upstream performance text ' + upstreamKey);
        check(catalogs.get(language).get(key) === expected[upstreamKey],
            language + ': performance text must match VRCX-0 ' + upstreamKey + ' (' + key + ')');
    }
}
check(simplified.get('Brightness') !== simplified.get('Value'), 'HSV brightness and parameter values need distinct translations');
console.log('[Pro localization validation] PASS: ' + checks + ' checks; ' + simplified.size + ' keys in each of five languages.');
