#!/usr/bin/env node
// Remove serialized controller objects that cannot be reached from the actual
// AnimatorController layer graph. Dry run by default; --apply updates only the
// eight built-in controllers and their byte-identical restore snapshots.
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
const rootOption = args.indexOf('--package-root');
const root = path.resolve(rootOption === -1 ? path.join(__dirname, '../..') : args[rootOption + 1]);
const apply = args.includes('--apply');
const names = ['GmgActionLayer', 'GmgAdditiveLayer', 'GmgBaseLayer', 'GmgFxLayer', 'GmgGestureLayer', 'GmgSittingLayer', 'GmgUtilityIKPose', 'GmgUtilityTPose'];
const directory = path.join(root, 'Resources/GestureManagerPro/Vrc3/Controllers');
const plans = [];
function requireCondition(condition, message) { if (!condition) throw new Error(message); }

for (const name of names) {
    const controller = path.join(directory, name + '.controller');
    const restore = path.join(directory, 'Restore', name + '.txt');
    const source = fs.readFileSync(controller, 'utf8');
    requireCondition(source === fs.readFileSync(restore, 'utf8'), `${name}: controller and restore snapshot differ before pruning.`);
    const guid = /^guid:\s*([a-f\d]{32})/mi.exec(fs.readFileSync(controller + '.meta', 'utf8'))?.[1];
    requireCondition(guid, `${name}: controller metadata has no GUID.`);
    const documents = source.split(/(?=^--- !u!)/m);
    const definitions = new Map();
    const roots = [];
    for (const document of documents) {
        const header = /^--- !u!(\d+) &(-?\d+)(?: stripped)?\r?\n/.exec(document);
        if (!header) {
            requireCondition(!document.startsWith('--- !u!'), `${name}: unsupported Unity object header.`);
            continue;
        }
        requireCondition(!definitions.has(header[2]), `${name}: duplicate local object ID ${header[2]}.`);
        definitions.set(header[2], document);
        if (header[1] === '91') roots.push(header[2]);
    }
    requireCondition(roots.length === 1, `${name}: exactly one AnimatorController root is required.`);
    const reachable = new Set();
    function visit(id) {
        if (id === '0' || reachable.has(id)) return;
        requireCondition(definitions.has(id), `${name}: reachable local reference ${id} has no object definition.`);
        reachable.add(id);
        const document = definitions.get(id);
        for (const reference of document.matchAll(/\{fileID:\s*(-?\d+)([^{}]*)\}/g)) {
            const referenceGuid = /\bguid:\s*([a-f\d]{32})/.exec(reference[2])?.[1];
            if (!referenceGuid || referenceGuid === guid) visit(reference[1]);
        }
    }
    roots.forEach(visit);
    const kept = documents.filter(document => {
        const header = /^--- !u!\d+ &(-?\d+)/.exec(document);
        return !header || reachable.has(header[1]);
    });
    const result = kept.join('');
    const actualKept = new Map(result.split(/(?=^--- !u!)/m).flatMap(document => {
        const header = /^--- !u!\d+ &(-?\d+)/.exec(document);
        return header ? [[header[1], document]] : [];
    }));
    requireCondition(actualKept.size === reachable.size, `${name}: kept-object count mismatch.`);
    for (const id of reachable)
        requireCondition(actualKept.get(id) === definitions.get(id), `${name}: reachable object ${id} body changed.`);
    const before = Buffer.byteLength(source, 'utf8');
    const after = Buffer.byteLength(result, 'utf8');
    plans.push({ name, controller, restore, source, result, objects: definitions.size, kept: reachable.size, before, after });
}

// Complete all graph and integrity checks before writing the first asset.
if (apply) {
    for (const plan of plans)
        requireCondition(fs.readFileSync(plan.controller, 'utf8') === plan.source && fs.readFileSync(plan.restore, 'utf8') === plan.source,
            `${plan.name}: asset changed during analysis; no writes were performed.`);
    for (const plan of plans) {
        if (plan.source === plan.result) continue;
        fs.writeFileSync(plan.controller, plan.result, 'utf8');
        fs.writeFileSync(plan.restore, plan.result, 'utf8');
    }
}
for (const plan of plans)
    console.log(`${apply ? 'APPLIED' : 'PREVIEW'} ${plan.name}: ${plan.objects} -> ${plan.kept} objects; ${plan.before} -> ${plan.after} bytes per controller/snapshot; every reachable object body preserved.`);
const removedObjects = plans.reduce((total, plan) => total + plan.objects - plan.kept, 0);
const removedBytes = plans.reduce((total, plan) => total + (plan.before - plan.after) * 2, 0);
console.log(`[Controller pruning] ${apply ? 'PASS' : 'DRY RUN'}: ${removedObjects} unreachable object blocks; ${removedBytes} bytes removed across ${plans.length * 2} assets.`);
