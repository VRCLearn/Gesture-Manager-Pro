#!/usr/bin/env node
// Read-only controller analysis. Prints unknown external reference context and
// writes a report only when --output is explicitly supplied.
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const root = path.resolve(option('--package-root', path.join(__dirname, '../..')));
const sdkOption = option('--sdk-root', null);
if (!sdkOption) {
    console.error('Usage: node .github/scripts/Inspect-ControllerReferences.cjs --sdk-root <com.vrchat.avatars-directory> [--package-root <package-directory>] [--output <report.json>]');
    process.exit(2);
}
const sdk = path.resolve(sdkOption);
function walk(dir) { return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? e.name.startsWith('.') || e.name.endsWith('~') ? [] : walk(path.join(dir, e.name)) : [path.join(dir, e.name)]); }
const known = new Map();
for (const file of [...walk(root), ...walk(sdk)].filter(file => file.endsWith('.meta'))) {
    const match = /^guid:\s*([a-f\d]{32})/mi.exec(fs.readFileSync(file, 'utf8'));
    if (match) known.set(match[1], file.slice(0, -5));
}
const report = new Map();
const graphSummary = [];
const motionExpression = /\{fileID:\s*(-?\d+),\s*guid:\s*([a-f\d]{32}),\s*type:\s*(\d+)\}/g;
const field = (block, name) => new RegExp(`^\\s*${name}:\\s*(.*)$`, 'm').exec(block)?.[1]?.trim();
for (const file of walk(path.join(root, 'Resources/GestureManagerPro/Vrc3/Controllers')).filter(file => file.endsWith('.controller'))) {
    const source = fs.readFileSync(file, 'utf8');
    const blocks = source.split(/(?=^--- !u!)/m);
    const localObjects = new Map(blocks.flatMap(block => {
        const id = /^--- !u!\d+ &(-?\d+)/.exec(block)?.[1];
        return id ? [[id, block]] : [];
    }));
    const controllerGuid = /^guid:\s*([a-f\d]{32})/mi.exec(fs.readFileSync(file + '.meta', 'utf8'))[1];
    const reachable = new Set();
    function visit(id) {
        if (reachable.has(id) || !localObjects.has(id)) return;
        reachable.add(id);
        for (const match of localObjects.get(id).matchAll(/\{fileID:\s*(-?\d+)([^{}]*)\}/g)) {
            const guid = /guid:\s*([a-f\d]{32})/.exec(match[2])?.[1];
            if (!guid || guid === controllerGuid) visit(match[1]);
        }
    }
    for (const block of blocks) if (/^--- !u!91 /.test(block)) visit(/^--- !u!91 &(-?\d+)/.exec(block)[1]);
    graphSummary.push({ file: path.basename(file), totalObjects: localObjects.size, reachableObjects: reachable.size,
        orphanObjects: [...localObjects.keys()].filter(id => !reachable.has(id)) });
    const preview = option('--prune-preview', null);
    if (preview) {
        const previewRoot = path.resolve(preview);
        fs.mkdirSync(previewRoot, { recursive: true });
        const kept = blocks.filter(block => {
            const id = /^--- !u!\d+ &(-?\d+)/.exec(block)?.[1];
            return !id || reachable.has(id);
        }).join('');
        fs.writeFileSync(path.join(previewRoot, path.basename(file)), kept);
    }
    for (const block of blocks) {
        const name = field(block, 'm_Name') || '(unnamed)';
        const category = /^--- !u!(\d+)/.exec(block)?.[1] || 'header';
        const children = block.split(/^  - serializedVersion: \d+\s*$/m);
        for (const match of block.matchAll(motionExpression)) {
            const [reference, fileId, guid] = match;
            if (known.has(guid)) continue;
            const property = /([\w]+):\s*$/.exec(block.slice(0, match.index))?.[1] || '(array field)';
            const key = `${guid}:${fileId}`;
            if (!report.has(key)) report.set(key, { guid, fileId, property, contexts: [] });
            const owner = children.find(child => child.includes(reference));
            const context = {
                file: path.basename(file), classId: category, objectName: name,
                objectId: /^--- !u!\d+ &(-?\d+)/.exec(block)?.[1],
                reachable: reachable.has(/^--- !u!\d+ &(-?\d+)/.exec(block)?.[1]),
                position: field(owner || '', 'm_Position'), threshold: field(owner || '', 'm_Threshold'),
                mirror: field(owner || '', 'm_Mirror'), timeScale: field(owner || '', 'm_TimeScale'),
                fields: property === 'm_Script' ? block.split(/\r?\n/).filter(line => /^  [A-Za-z_]\w*:/.test(line) && !/^  m_/.test(line)).map(line => line.trim()) : undefined
            };
            const item = report.get(key);
            if (!item.contexts.some(previous => JSON.stringify(previous) === JSON.stringify(context))) item.contexts.push(context);
        }
    }
}
const items = [...report.values()];
const output = option('--output', null);
if (output) fs.writeFileSync(path.resolve(output), JSON.stringify(items, null, 2) + '\n');
console.log(`[Controller references] ${items.length} unresolved reference tuples.`);
console.log(`[Controller references] ${items.filter(item => item.contexts.some(context => context.reachable)).length} unresolved tuples are reachable from the controller's layer graph.`);
for (const item of graphSummary) console.log(`[Controller graph] ${item.file}: ${item.reachableObjects}/${item.totalObjects} objects reachable; ${item.orphanObjects.length} orphan objects.`);
for (const item of items) {
    console.log(`${item.guid}:${item.fileId} ${item.property}`);
    for (const context of item.contexts.slice(0, 5)) console.log('  ' + JSON.stringify(context));
    if (item.contexts.length > 5) console.log(`  ... ${item.contexts.length - 5} more contexts`);
}
