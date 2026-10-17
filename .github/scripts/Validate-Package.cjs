#!/usr/bin/env node
// Validates the distributable package tree; no Unity or third-party libraries required.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const args = process.argv.slice(2);
const option = (name, fallback) => {
    const index = args.indexOf(name);
    return index === -1 ? fallback : args[index + 1];
};
const root = path.resolve(option('--package-root', path.join(__dirname, '../..')));
const explicitOriginal = option('--original-root', null);
const parent = path.dirname(root);
const parentManifest = path.join(parent, 'package.json');
const original = explicitOriginal ? path.resolve(explicitOriginal) :
    fs.existsSync(parentManifest) && JSON.parse(fs.readFileSync(parentManifest, 'utf8')).name === 'vrchat.blackstartx.gesture-manager' && fs.existsSync(path.join(parent, 'Scripts')) ? parent : null;
const dependencyRoots = args.flatMap((value, index) => value === '--dependency-root' && args[index + 1] ? [path.resolve(args[index + 1])] : []);
let passed = 0;
const failures = [];
function check(condition, message) {
    if (condition) passed++;
    else failures.push(message);
}
function walk(directory) {
    if (!fs.existsSync(directory)) return [];
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        if (entry.name.startsWith('.') || entry.name.endsWith('~')) return [];
        const item = path.join(directory, entry.name);
        return entry.isDirectory() ? walk(item) : [item];
    });
}
const read = file => fs.readFileSync(file, 'utf8');
const relative = file => path.relative(root, file).replaceAll('\\', '/');
const files = walk(root);
const metadata = files.filter(file => file.endsWith('.meta'));
const guids = new Map();
for (const file of metadata) {
    const match = /^guid:\s*([a-f\d]{32})\s*$/mi.exec(read(file));
    check(!!match, `Missing or malformed GUID: ${relative(file)}`);
    if (!match) continue;
    check(!guids.has(match[1]), `Duplicate GUID: ${relative(file)} and ${guids.get(match[1])}`);
    guids.set(match[1], relative(file));
    check(fs.existsSync(file.slice(0, -5)), `Orphan metadata: ${relative(file)}`);
}
const originalFiles = original ? ['Scripts', 'Resources'].flatMap(directory => walk(path.join(original, directory))) : [];
if (original && fs.existsSync(original))
    for (const entry of fs.readdirSync(original, { withFileTypes: true }))
        if (entry.isFile() && entry.name.endsWith('.meta')) originalFiles.push(path.join(original, entry.name));
const originalGuids = new Set(originalFiles.filter(file => file.endsWith('.meta')).flatMap(file => {
    const match = /^guid:\s*([a-f\d]{32})\s*$/mi.exec(read(file));
    return match ? [match[1]] : [];
}));
if (explicitOriginal) check(originalGuids.size > 0, 'Explicit original-root must contain original Unity asset metadata for the coexistence check.');
for (const [guid, file] of guids) check(!originalGuids.has(guid), `Original asset GUID reused: ${file}`);

const importable = /\.(?:cs|asmdef|uxml|uss|dll|png|jpg|jpeg|prefab|asset|controller|overrideController|anim|mat|shader|fbx|obj|wav)$/i;
for (const file of files.filter(file => importable.test(file)))
    check(fs.existsSync(file + '.meta'), `Missing asset metadata: ${relative(file)}`);

const yamlFiles = files.filter(file => /\.(?:prefab|asset|controller|overrideController|anim|mat)$/i.test(file) || file.endsWith('.txt') && read(file).startsWith('%YAML'));
const dependencyGuids = new Set(dependencyRoots.flatMap(walk).filter(file => file.endsWith('.meta')).flatMap(file => {
    const match = /^guid:\s*([a-f\d]{32})\s*$/mi.exec(read(file));
    return match ? [match[1]] : [];
}));
let yamlReferences = 0;
const inheritedMissing = new Set();
for (const file of yamlFiles) {
    const originalPath = original ? path.join(original, relative(file).replace(/^Resources\/GestureManagerPro\//, 'Resources/')) : null;
    const originalSource = originalPath && fs.existsSync(originalPath) ? read(originalPath) : '';
    const baselineCounts = new Map();
    for (const match of originalSource.matchAll(/\bguid:\s*([a-f\d]{32})/gi))
        baselineCounts.set(match[1], (baselineCounts.get(match[1]) || 0) + 1);
    for (const match of read(file).matchAll(/\bguid:\s*([a-f\d]{32})/gi)) {
        const guid = match[1].toLowerCase();
        // Unity's built-in resources use reserved d/e/f GUIDs.
        if (/^0{16}[def]0{15}$/.test(guid) || /^0{32}$/.test(guid)) continue;
        yamlReferences++;
        if (!guids.has(guid) && !dependencyGuids.has(guid) && baselineCounts.get(guid) > 0) {
            // Inherited controllers reference historical SDK animations absent
            // from the SDK 3 fixture. Record the baseline without accepting any
            // new missing asset reference introduced by the Pro package.
            inheritedMissing.add(`${guid}: ${relative(file)}`);
            baselineCounts.set(guid, baselineCounts.get(guid) - 1);
            continue;
        }
        check(guids.has(guid) || dependencyGuids.has(guid), `Unresolved YAML asset GUID ${guid}: ${relative(file)}`);
    }
}

const manifest = JSON.parse(read(path.join(root, 'package.json')));
check(manifest.name === 'com.vrclearn.gesture-manager-pro', 'Manifest has the standalone Pro package name.');
check(!Object.keys(manifest.vpmDependencies || {}).some(name => /blackstartx|gesture-manager-extensions/i.test(name)), 'Package still depends on the original Gesture Manager or extension.');
check(Object.hasOwn(manifest.vpmDependencies || {}, 'com.vrchat.avatars'), 'VRChat Avatars SDK dependency declared.');
check(manifest.license === 'SEE LICENSE IN LICENSING.md', 'Manifest points to the AGPL grant and limited linking permission.');
const projectLicense = path.join(root, 'LICENSE.md');
check(fs.existsSync(projectLicense) && crypto.createHash('sha256').update(read(projectLicense).replaceAll('\r\n', '\n')).digest('hex') === '0d96a4ff68ad6d4b6f1f30f713b18d5184912ba8dd389f86aa7710db079abcb0', 'Complete GNU AGPL version 3 license text included.');
const licensingNotice = path.join(root, 'LICENSING.md');
check(fs.existsSync(licensingNotice) && /AGPL-3\.0-only/.test(read(licensingNotice)) && /Additional permission under GNU AGPL version 3 section 7/.test(read(licensingNotice)), 'Version-3-only grant and limited SDK linking permission included.');
const upstreamLicense = path.join(root, 'ThirdParty/GestureManager/LICENSE.txt');
check(fs.existsSync(upstreamLicense) && /BlackStartx/.test(read(upstreamLicense)) && /Permission is hereby granted, free of charge/.test(read(upstreamLicense)), 'Upstream MIT copyright and permission notice included.');
check(fs.existsSync(path.join(root, 'ThirdParty/OVRLipSync/LICENSE.txt')), 'Meta license included.');
check(fs.existsSync(path.join(root, 'ThirdParty/OVRLipSync/THIRD_PARTY_NOTICES.txt')), 'Meta third-party notices included.');

const sourceFiles = files.filter(file => /\.(?:cs|asmdef|uxml|uss)$/i.test(file));
for (const file of sourceFiles) {
    const source = read(file);
    check(!/BlackStartX\.GestureManager|Cqmhv\.GestureManager\.Extensions|vrchat\.blackstartx\.gesture-manager|HarmonyLib|GestureManagerLocalizationPatch/.test(source), `Old package or patch reference: ${relative(file)}`);
    if (file.endsWith('.asmdef')) {
        const assembly = JSON.parse(source);
        check(assembly.name.startsWith('VRCLearn.GestureManagerPro'), `Non-Pro assembly: ${relative(file)}`);
        check(!(assembly.references || []).some(reference => /blackstartx|Harmony|Cqmhv/i.test(reference)), `Old assembly dependency: ${relative(file)}`);
    }
    if (file.endsWith('.cs')) {
        for (const match of source.matchAll(/const\s+string\s+(\w+)\s*=\s*"([^"\r\n]+)"/g))
            if (new RegExp(`Resources\\.Load(?:<[^>]+>)?\\(\\s*${match[1]}\\b`).test(source))
                check(match[2].startsWith('GestureManagerPro/'), `Unscoped resource path constant ${match[1]}: ${relative(file)}`);
        for (const match of source.matchAll(/Resources\.Load(?:<[^>]+>)?\(\s*"([^"\r\n]+)"/g)) {
            check(match[1].startsWith('GestureManagerPro/'), `Unscoped resource lookup ${match[1]}: ${relative(file)}`);
            const resource = path.join(root, 'Resources', match[1]);
            check(files.some(candidate => candidate.startsWith(resource + '.') && !candidate.endsWith('.meta')), `Resource lookup cannot resolve ${match[1]}: ${relative(file)}`);
        }
    }
}
const library = path.join(root, 'ThirdParty/OVRLipSync/Plugins/Win64/VRCLearnOVRLipSync.dll');
check(fs.existsSync(library), 'Bundled native analyzer exists.');
if (fs.existsSync(library)) {
    const hash = crypto.createHash('sha256').update(fs.readFileSync(library)).digest('hex');
    check(hash === '2318c42eb806753e340b426d0b83dd3278f5b8d3b851ccf90a5be0ea8e1d2cd3', 'Native analyzer differs from the accepted official SDK DLL.');
    const importer = read(library + '.meta');
    check(/isExplicitlyReferenced:\s*0/.test(importer), 'Native importer explicit-reference setting is unexpected.');
    check(/CPU:\s*x86_64/.test(importer) && /OS:\s*Windows/.test(importer), 'Native importer must target the Windows x64 Editor.');
    check(/Any:[\s\S]*?enabled:\s*0/.test(importer), 'Native library must not be enabled for all platforms.');
}
const uniqueFailures = [...new Set(failures)];
console.log(`[Pro package validation] ${failures.length ? 'FAIL' : 'PASS'}: ${passed} checks; ${metadata.length} GUIDs; ${yamlReferences} YAML references; ${sourceFiles.length} source/assets scanned; ${dependencyGuids.size} dependency GUIDs.`);
if (!originalGuids.size) console.log('[Pro package validation] SKIP: no original Gesture Manager checkout supplied; coexistence GUID comparison and inherited-reference classification were not performed.');
if (inheritedMissing.size) console.log(`[Pro package validation] BASELINE: ${inheritedMissing.size} distinct historical SDK references are unresolved in the supplied fixture and also present in the original controller at the same path. These are not counted as passing resolved references.`);
if (args.includes('--report-baseline')) {
    const byFile = new Map();
    for (const item of inheritedMissing) {
        const [guid, file] = item.split(': ');
        if (!byFile.has(file)) byFile.set(file, []);
        byFile.get(file).push(guid);
    }
    for (const [file, refs] of byFile) console.log(`[Pro baseline] ${file}: ${refs.join(', ')}`);
}
for (const failure of uniqueFailures.slice(0, 60)) console.error(`- ${failure}`);
if (uniqueFailures.length > 60) console.error(`- ${uniqueFailures.length - 60} more distinct failures.`);
process.exitCode = failures.length ? 1 : 0;
