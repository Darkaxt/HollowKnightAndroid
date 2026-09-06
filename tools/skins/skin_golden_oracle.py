"""Independent stdlib-only bounded descriptor wire goldens.

All contracts stop at 64 KiB. Only descriptor-interlock-v1 decides non-CLEAR.
No file authenticity, stress/preallocation parity, live binding, or runtime
catalog authority is claimed; the catalog source is checked independently.
"""
import base64
from functools import lru_cache
import unicodedata
import hashlib
import json
from pathlib import Path
import re

CONTRACT = 'descriptor-empty-clear-v1'
EXPECTATIONS = {'descriptorId', 'profileId', 'gameVersion', 'catalogId', 'catalogSha256', 'leaseId'}
AUTHORITY = {'profileId': 'hollow-knight', 'gameVersion': '1.5.12620', 'catalogId': 'hk-custom-knight-v3.5.0-205', 'catalogSha256': '258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a'}
UUID = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
HASH = re.compile(r'[0-9a-f]{64}')
DECIMAL = re.compile(r'0|[1-9][0-9]*')
ROOT_FIELDS = EXPECTATIONS | {'schemaVersion', 'sessionSequence', 'registryGenerationId', 'registryGenerationSha256', 'activation', 'packs', 'leaseTokenSha256'}


class FixtureFormatError(ValueError):
    """Broken golden harness data, not a negative descriptor verdict."""


class OracleOutOfScope(ValueError):
    """Recognized contract authority this bounded oracle does not decide."""


class _Invalid(ValueError):
    pass


def _require(condition, code):
    if not condition:
        raise _Invalid(code)


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        _require(key not in result, 'DUPLICATE_KEY')
        result[key] = value
    return result


def _no_number(value):
    raise _Invalid('JSON_SYNTAX')


def _integer(value):
    _require(value != '-0', 'JSON_SYNTAX')
    return int(value)


def _depth_guard(text):
    # Scan before json.loads: braces inside strings/escapes do not count.
    # Container nesting 33 can be an empty container at value depth 32.
    depth = 0
    quoted = escaped = False
    for char in text:
        if quoted:
            if escaped:
                escaped = False
            elif char == '\\':
                escaped = True
            elif char == '"':
                quoted = False
        elif char == '"':
            quoted = True
        elif char in '[{':
            depth += 1
            _require(depth <= 33, 'STRUCTURE_LIMIT')
        elif char in ']}':
            depth -= 1


def _structural(value, depth=0):
    _require(depth <= 32, 'STRUCTURE_LIMIT')
    count = 1
    if type(value) is dict:
        _require(len(value) <= 256, 'STRUCTURE_LIMIT')
        for key, child in value.items():
            key.encode('utf-8', errors='strict')
            count += 1 + _structural(child, depth + 1)
    elif type(value) is list:
        _require(len(value) <= 256, 'STRUCTURE_LIMIT')
        count += sum(_structural(child, depth + 1) for child in value)
    elif type(value) is str:
        value.encode('utf-8', errors='strict')
    _require(count <= 400000, 'STRUCTURE_LIMIT')
    return count


def _canonical(value):
    if type(value) is dict:
        return '{' + ','.join(_canonical(k) + ':' + _canonical(value[k]) for k in sorted(value, key=lambda k: k.encode('utf-16-be'))) + '}'
    if type(value) is list:
        return '[' + ','.join(map(_canonical, value)) + ']'
    # ensure_ascii=False preserves supplementary scalars; json uses lowercase
    # u escapes for remaining C0 and the required five short control escapes.
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False)


def _fields(value, required, optional=frozenset()):
    _require(type(value) is dict, 'TYPE')
    _require(required <= value.keys() and value.keys() <= required | optional, 'FIELDS')


def _matches(pattern, value, code):
    _require(type(value) is str and pattern.fullmatch(value) is not None, code)


def _decimal(value):
    _matches(DECIMAL, value, 'DECIMAL')
    _require(len(value) <= 19 and int(value) <= 9223372036854775807, 'DECIMAL')


def _has_null(value):
    if value is None:
        return True
    if type(value) is dict:
        return any(map(_has_null, value.values()))
    if type(value) is list:
        return any(map(_has_null, value))
    return False


CLEAR_CONTRACT = 'descriptor-clear-v1'
INTERLOCK_CONTRACT = 'descriptor-interlock-v1'
OPERATIONS = ('STARTUP_APPLY', 'MODE_ON', 'MODE_OFF', 'DEATH_ROTATION', 'REBIND_APPLY')
FAILURE_CODE = re.compile(r'[A-Z][A-Z0-9_]{0,127}')
PACK_ID = re.compile(r'[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?')
IDENTITY = ('treeSha256', 'contentSha256', 'importReceiptSha256')
OBJECT_VALUES = ('objectRoot', 'receiptPath', 'treeSha256', 'contentSha256', 'manifestSha256', 'importReceiptSha256')
# Kotlin trim uses Character.isWhitespace OR isSpaceChar, not Python isspace.
_TRIM_SPACE = frozenset(range(9, 14)) | frozenset(range(28, 33)) | {0xa0, 0x1680, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000} | frozenset(range(0x2000, 0x200b))
_BIDI = {0x61c, 0x200e, 0x200f, 0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066, 0x2067, 0x2068, 0x2069}


def _catalog_bytes(raw):
    if len(raw) != 4383 or hashlib.sha256(raw).hexdigest() != AUTHORITY['catalogSha256']:
        raise FixtureFormatError('Normative catalog bytes/digest mismatch')
    try:
        text = raw.decode('utf-8', errors='strict')
    except UnicodeError as error:
        raise FixtureFormatError('Normative catalog UTF-8 failure') from error
    rows = tuple(text[:-1].split('\n'))
    if not text.endswith('\n') or '\r' in text or '﻿' in text or len(rows) != 205 or len(set(rows)) != 205 or not all(rows):
        raise FixtureFormatError('Normative catalog framing/count mismatch')
    return rows


@lru_cache(maxsize=1)
def _catalog():
    path = Path(__file__).resolve().parents[2] / 'docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt'
    try:
        raw = path.read_bytes()
    except OSError as error:
        raise FixtureFormatError('Cannot read normative catalog') from error
    return _catalog_bytes(raw)


def _display(value):
    _require(type(value) is str and 1 <= len(value) <= 80, 'DISPLAY')
    _require(ord(value[0]) not in _TRIM_SPACE and ord(value[-1]) not in _TRIM_SPACE, 'DISPLAY')
    _require(unicodedata.normalize('NFKC', value) == value, 'DISPLAY')
    _require(not any(ord(c) < 32 or 127 <= ord(c) <= 159 or ord(c) in _BIDI or c in '/\\' for c in value), 'DISPLAY')


def _object(value, catalog):
    _fields(value, set(OBJECT_VALUES) | {'textures'})
    for key in OBJECT_VALUES[2:]:
        _matches(HASH, value[key], 'DIGEST')
    for field, owner, digest in [('objectRoot', 'objects', value['treeSha256']), ('receiptPath', 'import-receipts', value['importReceiptSha256'])]:
        _require(value[field] == f'{owner}/sha256/{digest[:2]}/{digest}', 'OBJECT_PATH')
    textures = value['textures']
    _require(type(textures) is list and 1 <= len(textures) <= 205, 'TEXTURE_COUNT')
    previous = -1
    for texture in textures:
        _fields(texture, {'ordinal', 'target', 'sourceRelativePath', 'sourceSha256', 'length'})
        ordinal = texture['ordinal']
        _require(type(ordinal) is int and 0 <= ordinal <= 204 and ordinal > previous, 'ORDINAL')
        _require(texture['target'] == catalog[ordinal], 'TARGET')
        previous = ordinal
        _matches(HASH, texture['sourceSha256'], 'DIGEST')
        _decimal(texture['length'])
        _require(1 <= int(texture['length']) <= 16777216, 'TEXTURE_LENGTH')
        encoded = base64.b32encode(bytes.fromhex(texture['sourceSha256'])).decode('ascii').lower().rstrip('=')
        _require(texture['sourceRelativePath'] == 'pack/assets/' + encoded, 'SOURCE_PATH')


def _identity(value):
    return tuple(value[key] for key in IDENTITY)


def _clear_union(value):
    catalog = _catalog()
    packs, activation = value['packs'], value['activation']
    _require(len(packs) <= 64, 'PACK_COUNT')
    owners, ids, previous = set(), set(), None
    for pack in packs:
        _fields(pack, {'id', 'name', 'author', 'candidateKey', 'rotationEligible', 'currentObject'}, {'retainedActiveObject'})
        _matches(PACK_ID, pack['id'], 'PACK_ID')
        _require(pack['id'] not in ids, 'PACK_ID'); ids.add(pack['id'])
        _display(pack['name']); _display(pack['author'])
        order = (pack['name'].encode('utf-8'), pack['id'])
        _require(previous is None or previous < order, 'PACK_ORDER'); previous = order
        _matches(HASH, pack['candidateKey'], 'DIGEST')
        _require(pack['candidateKey'] not in owners, 'CANDIDATE'); owners.add(pack['candidateKey'])
        _require(type(pack['rotationEligible']) is bool, 'TYPE')
        _object(pack['currentObject'], catalog)
        if 'retainedActiveObject' in pack:
            _object(pack['retainedActiveObject'], catalog)
    visual = activation['active']
    required = {p['id'] for p in packs if p['rotationEligible']}
    if 'selectedPackId' in activation:
        _matches(PACK_ID, activation['selectedPackId'], 'PACK_ID')
        required.add(activation['selectedPackId'])
    if visual['kind'] == 'VANILLA':
        _fields(visual, {'kind'})
    else:
        _fields(visual, {'kind', 'id'} | set(IDENTITY))
        _matches(PACK_ID, visual['id'], 'PACK_ID')
        for key in IDENTITY:
            _matches(HASH, visual[key], 'DIGEST')
        required.add(visual['id'])
    _require(ids == required, 'PACK_UNION')
    for pack in packs:
        current = _identity(pack['currentObject'])
        old = visual['kind'] == 'PACK' and visual['id'] == pack['id'] and _identity(visual) != current
        retained = pack.get('retainedActiveObject')
        _require((retained is not None) == old, 'RETAINED')
        if old:
            _require(_identity(retained) == _identity(visual), 'RETAINED')


def _snapshot(value):
    _fields(value, {'mode', 'active', 'skinStamp'}, {'selectedPackId'})
    _require(type(value['mode']) is str and value['mode'] in ('OFF', 'ON', 'ROTATE'), 'ENUM')
    _decimal(value['skinStamp'])
    if 'selectedPackId' in value:
        _matches(PACK_ID, value['selectedPackId'], 'PACK_ID')
    visual = value['active']
    _require(type(visual) is dict, 'TYPE')
    _require('kind' in visual, 'FIELDS')
    _require(type(visual['kind']) is str and visual['kind'] in ('VANILLA', 'PACK'), 'ENUM')
    if visual['kind'] == 'VANILLA':
        _fields(visual, {'kind'})
    else:
        _fields(visual, {'kind', 'id'} | set(IDENTITY))
        _matches(PACK_ID, visual['id'], 'PACK_ID')
        for key in IDENTITY:
            _matches(HASH, visual[key], 'DIGEST')


def _interlock(value):
    activation = value['activation']; lock = activation['rotationInterlock']
    required = {'state', 'transactionId', 'operation', 'baseGenerationId', 'baseGenerationSha256', 'prior', 'target', 'bindingToken', 'priorEstablishedOnBinding'}
    if lock['state'] == 'ROLLBACK_FAILED':
        required |= {'originalFailure', 'rollbackFailure'}
    _fields(lock, required)
    for key in ('transactionId', 'baseGenerationId'):
        _matches(UUID, lock[key], 'UUID')
    _matches(HASH, lock['baseGenerationSha256'], 'DIGEST')
    _require(type(lock['operation']) is str and lock['operation'] in OPERATIONS, 'ENUM')
    _snapshot(lock['prior']); _snapshot(lock['target'])
    prior, target = lock['prior'], lock['target']
    _require(int(prior['skinStamp']) < 9223372036854775807 and int(target['skinStamp']) == int(prior['skinStamp']) + 1, 'STAMP_TRANSITION')
    _require({k: v for k, v in activation.items() if k != 'rotationInterlock'} == prior, 'PRIOR_MISMATCH')
    token = lock['bindingToken']
    _require(type(token) is str and 1 <= len(token) <= 256, 'BINDING')
    _require(ord(token[0]) not in _TRIM_SPACE and ord(token[-1]) not in _TRIM_SPACE, 'BINDING')
    _require(unicodedata.normalize('NFKC', token) == token, 'BINDING')
    _require(not any(ord(c) < 32 or 127 <= ord(c) <= 159 or ord(c) in _BIDI for c in token), 'BINDING')
    _require(type(lock['priorEstablishedOnBinding']) is bool, 'TYPE')
    if lock['state'] == 'ROLLBACK_FAILED':
        for key in ('originalFailure', 'rollbackFailure'):
            _matches(FAILURE_CODE, lock[key], 'FAILURE_CODE')
    _interlock_union(value, (activation, prior, target))


def _interlock_union(value, snapshots):
    packs = value['packs']; references = {}
    catalog = _catalog()
    _require(len(packs) <= 64, 'PACK_COUNT')
    owners, ids, previous = set(), set(), None
    for pack in packs:
        _fields(pack, {'id', 'name', 'author', 'candidateKey', 'rotationEligible', 'currentObject'}, {'retainedActiveObject'})
        _matches(PACK_ID, pack['id'], 'PACK_ID')
        _require(pack['id'] not in ids, 'PACK_ID'); ids.add(pack['id'])
        _display(pack['name']); _display(pack['author'])
        order = (pack['name'].encode('utf-8'), pack['id'])
        _require(previous is None or previous < order, 'PACK_ORDER'); previous = order
        _matches(HASH, pack['candidateKey'], 'DIGEST')
        _require(pack['candidateKey'] not in owners, 'CANDIDATE'); owners.add(pack['candidateKey'])
        _require(type(pack['rotationEligible']) is bool, 'TYPE')
        _object(pack['currentObject'], catalog)
        if 'retainedActiveObject' in pack:
            _object(pack['retainedActiveObject'], catalog)
    required = {p['id'] for p in packs if p['rotationEligible']}
    for snapshot in snapshots:
        if 'selectedPackId' in snapshot:
            required.add(snapshot['selectedPackId'])
        visual = snapshot['active']
        if visual['kind'] == 'PACK':
            required.add(visual['id'])
            references.setdefault(visual['id'], set()).add(_identity(visual))
    _require(ids == required, 'PACK_UNION')
    for pack in packs:
        historical = references.get(pack['id'], set()) - {_identity(pack['currentObject'])}
        retained = pack.get('retainedActiveObject')
        _require(len(historical) <= 1 and (retained is not None) == bool(historical), 'RETAINED')
        if historical:
            _require(_identity(retained) in historical, 'RETAINED')


def _project(value):
    activation = value['activation']; visual = activation['active']
    log = [
        ['root', value['schemaVersion'], value['descriptorId'], value['sessionSequence'], value['profileId'], value['gameVersion'], value['catalogId'], value['catalogSha256'], value['registryGenerationId'], value['registryGenerationSha256'], value['leaseId'], value['leaseTokenSha256']],
        ['activation', activation['mode'], activation.get('selectedPackId'), activation['skinStamp']],
        ['visual', 'activation', visual['kind']] + ([visual['id']] + [visual[k] for k in IDENTITY] if visual['kind'] == 'PACK' else []),
        ['interlock', activation['rotationInterlock']['state']], ['packs', len(value['packs'])],
    ]
    lock = activation['rotationInterlock']
    if lock['state'] != 'CLEAR':
        log.append(['transaction', lock['transactionId'], lock['operation'], lock['baseGenerationId'], lock['baseGenerationSha256']])
        for owner in ('prior', 'target'):
            snapshot = lock[owner]; visual = snapshot['active']
            log.append(['snapshot', owner, snapshot['mode'], snapshot.get('selectedPackId'), snapshot['skinStamp']])
            log.append(['visual', owner, visual['kind']] + ([visual['id']] + [visual[k] for k in IDENTITY] if visual['kind'] == 'PACK' else []))
        log.append(['binding', lock['bindingToken'], lock['priorEstablishedOnBinding']])
        if lock['state'] == 'ROLLBACK_FAILED':
            log.append(['failures', lock['originalFailure'], lock['rollbackFailure']])
    for i, pack in enumerate(value['packs']):
        log.append(['pack', i, pack['id'], pack['name'], pack['author'], pack['candidateKey'], pack['rotationEligible'], 'retainedActiveObject' in pack])
        for field, label in [('currentObject', 'current'), ('retainedActiveObject', 'retained')]:
            if field not in pack:
                continue
            obj = pack[field]
            log.append(['object', i, label] + [obj[k] for k in OBJECT_VALUES])
            log.append(['textures', i, label, len(obj['textures'])])
            for j, t in enumerate(obj['textures']):
                log.append(['texture', i, label, j, t['ordinal'], t['target'], t['sourceRelativePath'], t['sourceSha256'], t['length']])
    return log


def parse_launch_descriptor(raw, expected_sha256, expectations, *, contract):
    if contract not in (CONTRACT, CLEAR_CONTRACT, INTERLOCK_CONTRACT):
        raise OracleOutOfScope('Unknown oracle contract')
    if len(raw) > 65536:
        raise OracleOutOfScope('Descriptor exceeds this oracle\'s 64 KiB scope')
    try:
        _matches(HASH, expected_sha256, 'DIGEST')
        _require(hashlib.sha256(raw).hexdigest() == expected_sha256, 'HASH_MISMATCH')
        try:
            text = raw.decode('utf-8', errors='strict')
        except UnicodeDecodeError:
            raise _Invalid('UTF8') from None
        _require(not text.startswith('﻿'), 'NON_CANONICAL_BYTES')
        _depth_guard(text)
        try:
            value = json.loads(text, object_pairs_hook=_pairs, parse_int=_integer, parse_float=_no_number, parse_constant=_no_number)
            _structural(value)
            canonical = _canonical(value)
        except (json.JSONDecodeError, UnicodeError, RecursionError, ValueError) as error:
            if isinstance(error, _Invalid):
                raise
            raise _Invalid('JSON_SYNTAX') from None
        _require(canonical == text, 'NON_CANONICAL_BYTES')
        _require(not _has_null(value), 'NULL')
        _fields(value, ROOT_FIELDS)
        _require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1, 'SCHEMA')
        for key in ['descriptorId', 'registryGenerationId', 'leaseId']:
            _matches(UUID, value[key], 'UUID')
        for key in ['catalogSha256', 'registryGenerationSha256', 'leaseTokenSha256']:
            _matches(HASH, value[key], 'DIGEST')
        _decimal(value['sessionSequence'])
        _require(all(value[key] == expected for key, expected in AUTHORITY.items()), 'AUTHORITY')
        activation = value['activation']
        _fields(activation, {'mode', 'active', 'skinStamp', 'rotationInterlock'}, {'selectedPackId'})
        _require(type(activation['mode']) is str and activation['mode'] in ('OFF', 'ON', 'ROTATE'), 'ENUM')
        _decimal(activation['skinStamp'])
        visual, interlock = activation['active'], activation['rotationInterlock']
        _require(type(visual) is dict and type(interlock) is dict, 'TYPE')
        _require('kind' in visual and 'state' in interlock, 'FIELDS')
        _require(type(visual['kind']) is str and visual['kind'] in ('VANILLA', 'PACK'), 'ENUM')
        _require(type(interlock['state']) is str and interlock['state'] in ('CLEAR', 'ARMED', 'ROLLBACK_FAILED'), 'ENUM')
        _require(type(value['packs']) is list, 'TYPE')
        if (interlock['state'] != 'CLEAR' and contract != INTERLOCK_CONTRACT) or (contract == CONTRACT and value['packs']):
            raise OracleOutOfScope('Non-CLEAR requires descriptor-interlock-v1; nonempty packs require descriptor-clear-v1')
        if interlock['state'] != 'CLEAR':
            _interlock(value)
        elif contract == CONTRACT:
            _fields(interlock, {'state'})
            _require(visual['kind'] == 'VANILLA' and 'selectedPackId' not in activation, 'PACK_UNION')
            _fields(visual, {'kind'})
        else:
            _fields(interlock, {'state'})
            _clear_union(value)
        _fields(expectations, EXPECTATIONS)
        _require(all(value[key] == expectations[key] for key in EXPECTATIONS), 'EXPECTATION')
        return {'outcome': 'OK', 'semanticLog': _project(value)}
    except _Invalid as error:
        return {'outcome': 'DOCUMENT_INVALID', 'violation': str(error)}


def _fixture_require(condition, detail):
    if not condition:
        raise FixtureFormatError(detail)


def _fixture_fields(value, keys):
    _fixture_require(type(value) is dict and value.keys() == set(keys.split()), 'Unknown, missing, or wrong-kind fixture fields')


def _fixture_text(value):
    _fixture_require(type(value) is str and bool(value), 'Required nonempty fixture string')


def _log_schema(records, contract):
    _fixture_require(type(records) is list and len(records) >= 5, 'Missing semantic records')
    cursor = 0

    def record(tag, types):
        nonlocal cursor
        _fixture_require(cursor < len(records), 'Missing semantic record')
        row = records[cursor]; cursor += 1
        _fixture_require(type(row) is list and len(row) == len(types) + 1 and row[0] == tag, 'Malformed semantic record')
        for value, kind in zip(row[1:], types):
            if kind == 'optional':
                _fixture_require(value is None or type(value) is str and bool(value), 'Optional string required')
            else:
                _fixture_require(type(value) is kind, 'Wrong semantic value kind')
                if kind is str:
                    _fixture_text(value)
        return row

    def decimal(text):
        _fixture_require(DECIMAL.fullmatch(text) is not None and len(text) <= 19 and int(text) <= 9223372036854775807, 'Semantic decimal string required')

    root = record('root', [int] + [str] * 10)
    _fixture_require(root[1] == 1, 'Schema record required'); decimal(root[3])
    activation = record('activation', [str, 'optional', str]); decimal(activation[3])
    _fixture_require(activation[1] in ('OFF', 'ON', 'ROTATE'), 'Mode record required')
    _fixture_require(cursor < len(records) and type(records[cursor]) is list and len(records[cursor]) >= 3, 'Visual record required')
    kind = records[cursor][2]
    _fixture_require(kind in ('VANILLA', 'PACK'), 'Visual kind required')
    visual = record('visual', [str] * (2 if kind == 'VANILLA' else 6))
    _fixture_require(visual[1] == 'activation', 'Visual owner required')
    state = record('interlock', [str])[1]
    _fixture_require(state in (('CLEAR', 'ARMED', 'ROLLBACK_FAILED') if contract == INTERLOCK_CONTRACT else ('CLEAR',)), 'Interlock state required')
    count = record('packs', [int])[1]
    _fixture_require(0 <= count <= 64, 'Pack count required')
    if state != 'CLEAR':
        transaction = record('transaction', [str] * 4)
        _fixture_require(UUID.fullmatch(transaction[1]) is not None and UUID.fullmatch(transaction[3]) is not None and HASH.fullmatch(transaction[4]) is not None and transaction[2] in OPERATIONS, 'Transaction fields required')
        for owner in ('prior', 'target'):
            snapshot = record('snapshot', [str, str, 'optional', str])
            _fixture_require(snapshot[1] == owner and snapshot[2] in ('OFF', 'ON', 'ROTATE'), 'Snapshot owner/mode required'); decimal(snapshot[4])
            _fixture_require(cursor < len(records) and type(records[cursor]) is list and len(records[cursor]) >= 3, 'Snapshot visual required')
            visual_kind = records[cursor][2]
            _fixture_require(visual_kind in ('VANILLA', 'PACK'), 'Snapshot visual kind required')
            row = record('visual', [str] * (2 if visual_kind == 'VANILLA' else 6))
            _fixture_require(row[1] == owner, 'Snapshot visual owner required')
            if visual_kind == 'PACK':
                _fixture_require(PACK_ID.fullmatch(row[3]) is not None and all(HASH.fullmatch(x) is not None for x in row[4:]), 'Snapshot visual identity required')
            _fixture_require(snapshot[3] is None or PACK_ID.fullmatch(snapshot[3]) is not None, 'Snapshot selection required')
        binding = record('binding', [str, bool])
        token = binding[1]
        _fixture_require(1 <= len(token) <= 256 and ord(token[0]) not in _TRIM_SPACE and ord(token[-1]) not in _TRIM_SPACE and unicodedata.normalize('NFKC', token) == token and not any(ord(c) < 32 or 127 <= ord(c) <= 159 or ord(c) in _BIDI or 0xd800 <= ord(c) <= 0xdfff for c in token), 'Binding token required')
        if state == 'ROLLBACK_FAILED':
            failures = record('failures', [str, str])
            _fixture_require(all(FAILURE_CODE.fullmatch(x) is not None for x in failures[1:]), 'Failure codes required')
    if contract == CONTRACT:
        _fixture_require(count == 0 and kind == 'VANILLA' and activation[2] is None, 'Empty contract log required')
    for i in range(count):
        pack = record('pack', [int, str, str, str, str, bool, bool])
        _fixture_require(pack[1] == i, 'Ordered pack index required')
        for label in (('current', 'retained') if pack[7] else ('current',)):
            obj = record('object', [int] + [str] * 7)
            _fixture_require(obj[1:3] == [i, label], 'Object owner/order required')
            textures = record('textures', [int, str, int])
            _fixture_require(textures[1:3] == [i, label] and 1 <= textures[3] <= 205, 'Texture count/owner required')
            for j in range(textures[3]):
                texture = record('texture', [int, str, int, int, str, str, str, str])
                _fixture_require(texture[1:4] == [i, label, j] and 0 <= texture[4] <= 204, 'Texture owner/index required')
                decimal(texture[8])
    _fixture_require(cursor == len(records), 'Unhandled semantic records')


def _validate_case(case):
    _fixture_fields(case, 'caseId oracleContract input inputSha256 expectedSha256 expectations expected')
    _fixture_text(case['caseId'])
    _fixture_require(case['oracleContract'] in (CONTRACT, CLEAR_CONTRACT, INTERLOCK_CONTRACT), 'Unknown oracle contract')
    source = case['input']
    _fixture_require(type(source) is dict and set(source) in ({'utf8Text'}, {'utf8Hex'}), 'Exactly one exact-byte input required')
    try:
        if 'utf8Text' in source:
            _fixture_require(type(source['utf8Text']) is str, 'utf8Text must be a string')
            raw = source['utf8Text'].encode('utf-8', errors='strict')
        else:
            _fixture_require(type(source['utf8Hex']) is str and re.fullmatch(r'(?:[0-9a-f]{2})*', source['utf8Hex']) is not None, 'utf8Hex must be lowercase even hex')
            raw = bytes.fromhex(source['utf8Hex'])
    except UnicodeError as error:
        raise FixtureFormatError('Invalid fixture text Unicode') from error
    for key in ('inputSha256', 'expectedSha256'):
        _fixture_require(type(case[key]) is str and HASH.fullmatch(case[key]) is not None, 'Invalid fixture digest')
    _fixture_require(hashlib.sha256(raw).hexdigest() == case['inputSha256'], 'Exact inputSha256 mismatch before parser invocation')
    _fixture_fields(case['expectations'], 'descriptorId profileId gameVersion catalogId catalogSha256 leaseId')
    for key, value in case['expectations'].items():
        _fixture_text(value)
        if key in ('descriptorId', 'leaseId'):
            _fixture_require(UUID.fullmatch(value) is not None, 'Malformed expectation UUID')
        if key == 'catalogSha256':
            _fixture_require(HASH.fullmatch(value) is not None, 'Malformed expectation digest')
    expected = case['expected']
    _fixture_require(type(expected) is dict, 'Expected must be object')
    if expected.get('outcome') == 'OK':
        _fixture_fields(expected, 'outcome semanticLog')
        _log_schema(expected['semanticLog'], case['oracleContract'])
    else:
        _fixture_fields(expected, 'outcome violation')
        _fixture_require(expected['outcome'] == 'DOCUMENT_INVALID', 'Unknown expected outcome')
        _fixture_require(type(expected['violation']) is str and re.fullmatch(r'[A-Z][A-Z0-9_]*', expected['violation']) is not None, 'Stable violation required')
    return raw


def load_fixture(path):
    try:
        text = Path(path).read_bytes().decode('utf-8', errors='strict')
        _depth_guard(text)
        root = json.loads(text, object_pairs_hook=_pairs, parse_int=_integer, parse_float=_no_number, parse_constant=_no_number)
    except (ValueError, UnicodeError, RecursionError) as error:
        raise FixtureFormatError('Invalid outer fixture JSON') from error
    _fixture_fields(root, 'schemaVersion cases')
    _fixture_require(type(root['schemaVersion']) is int and root['schemaVersion'] == 1, 'Unknown fixture schema')
    cases = root['cases']
    _fixture_require(type(cases) is list and bool(cases), 'No handled cases')
    ids = set()
    for case in cases:
        _validate_case(case)
        _fixture_require(case['caseId'] not in ids, 'Duplicate case ID')
        ids.add(case['caseId'])
    return cases


def verify_case(case):
    raw = _validate_case(case)
    actual = parse_launch_descriptor(raw, case['expectedSha256'], case['expectations'], contract=case['oracleContract'])
    if actual != case['expected']:
        raise AssertionError(f"{case['caseId']}: expected {case['expected']!r}, got {actual!r}")


# Independent lifecycle-core-v1 harness. These are HARNESS limits, not reducer limits.
LIFECYCLE_CONTRACT = 'lifecycle-core-v1'
LIFECYCLE_MAX_BYTES = 65536
LIFECYCLE_MAX_EVENTS = 32
_LC_STATE = 'armedHero pendingEpoch stableCount currentHero currentSkin lastConfirmedEpoch armedSkin armedOccurrence occurrenceHighWater'
_LC_FLAGS = 'acceptingInput fullDamageMode canTakeDamage playable paused cutscene sceneTransition'
_LC_DIAGNOSES = frozenset('invalid-state same-binding rebound unbound stale-binding invalid-occurrence consumed-occurrence candidate-already-armed pending-epoch armed unarmed-after-death mismatched-occurrence death-confirmed stale-observation no-pending-epoch unstable-observation stabilizing stable-respawn epoch-overflow'.split())


def _lc_json(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            _fixture_require(key not in result, 'Duplicate lifecycle JSON key')
            result[key] = value
        return result
    def reject(value):
        raise FixtureFormatError('Lifecycle JSON requires integer numbers')
    def integer(value):
        _fixture_require(re.fullmatch(r'0|-?[1-9][0-9]*', value) is not None, 'Noncanonical integer')
        return int(value)
    try:
        value = json.loads(raw.decode('utf-8', errors='strict'), object_pairs_hook=pairs, parse_int=integer, parse_float=reject, parse_constant=reject)
        def unicode_values(v):
            if type(v) is str: v.encode('utf-8', errors='strict')
            elif type(v) is list:
                for child in v: unicode_values(child)
            elif type(v) is dict:
                for k, child in v.items(): unicode_values(k); unicode_values(child)
        unicode_values(value)
        return value
    except (ValueError, UnicodeError, RecursionError) as error:
        raise FixtureFormatError('Malformed lifecycle JSON') from error


def _lc_string(value):
    _fixture_require(type(value) is str, 'Lifecycle token must be a string')
    try: value.encode('utf-8', errors='strict')
    except UnicodeError as error: raise FixtureFormatError('Invalid lifecycle Unicode') from error


def _lc_uint(value):
    _lc_string(value)
    _fixture_require(re.fullmatch(r'0|[1-9][0-9]*', value) is not None and len(value) <= 20 and int(value) <= 18446744073709551615, 'Lifecycle unsigned decimal string')


def _lc_int(value):
    _fixture_require(type(value) is int and -2147483648 <= value <= 2147483647, 'Lifecycle signed Int32 required')


def _lc_state(state):
    # Representability only: malformed invariants deliberately reach observe_lifecycle.
    _fixture_fields(state, _LC_STATE)
    for key in ('armedHero', 'armedSkin', 'currentHero', 'currentSkin'):
        if state[key] is not None: _lc_string(state[key])
    for key in ('pendingEpoch', 'armedOccurrence'):
        if state[key] is not None: _lc_uint(state[key])
    for key in ('lastConfirmedEpoch', 'occurrenceHighWater'): _lc_uint(state[key])
    _lc_int(state['stableCount'])


def _lc_signal(signal):
    _fixture_require(type(signal) is dict and type(signal.get('type')) is str, 'Lifecycle signal object/type')
    kind = signal['type']
    _fixture_require(kind in ('Rebind', 'BeforeDeath', 'AfterDeath', 'Update'), 'Unknown lifecycle signal')
    suffix = ' occurrence' if kind in ('BeforeDeath', 'AfterDeath') else ' health ' + _LC_FLAGS if kind == 'Update' else ''
    _fixture_fields(signal, 'type hero skin' + suffix)
    _lc_string(signal['hero']); _lc_string(signal['skin'])
    if kind in ('BeforeDeath', 'AfterDeath'): _lc_uint(signal['occurrence'])
    if kind == 'Update':
        _lc_int(signal['health'])
        for key in _LC_FLAGS.split(): _fixture_require(type(signal[key]) is bool, 'Lifecycle predicate must be boolean')


def _lc_case(case):
    _fixture_fields(case, 'caseId oracleContract input inputSha256 expected')
    _fixture_text(case['caseId']); _lc_string(case['oracleContract'])
    if case['oracleContract'] != LIFECYCLE_CONTRACT: raise OracleOutOfScope('Unknown lifecycle contract')
    source = case['input']
    _fixture_require(type(source) is dict and set(source) in ({'utf8Text'}, {'utf8Hex'}), 'Exactly one lifecycle input encoding')
    value = next(iter(source.values())); _lc_string(value)
    if 'utf8Hex' in source:
        _fixture_require(re.fullmatch(r'(?:[0-9a-f]{2})*', value) is not None, 'Lifecycle hex encoding')
        raw = bytes.fromhex(value)
    else: raw = value.encode('utf-8')
    _fixture_require(type(case['inputSha256']) is str and re.fullmatch('[0-9a-f]{64}', case['inputSha256']) is not None, 'Lifecycle digest shape')
    _fixture_require(hashlib.sha256(raw).hexdigest() == case['inputSha256'], 'Lifecycle raw hash mismatch before decode')
    if len(raw) > LIFECYCLE_MAX_BYTES: raise OracleOutOfScope('Lifecycle harness 64 KiB input limit')
    data = _lc_json(raw); _fixture_fields(data, 'initialState events'); _lc_state(data['initialState'])
    events = data['events']; _fixture_require(type(events) is list and bool(events), 'Missing lifecycle events')
    if len(events) > LIFECYCLE_MAX_EVENTS: raise OracleOutOfScope('Lifecycle harness 32 event limit')
    for signal in events: _lc_signal(signal)
    records = case['expected']; _fixture_require(type(records) is list and len(records) == len(events), 'Full ordered lifecycle records required')
    for i, record in enumerate(records):
        _fixture_fields(record, 'step state diagnosis confirmedEpoch stableToken')
        _lc_int(record['step']); _fixture_require(record['step'] == i, 'Lifecycle step order')
        _lc_state(record['state']); _lc_string(record['diagnosis'])
        _fixture_require(record['diagnosis'] in _LC_DIAGNOSES, 'Unknown lifecycle diagnosis')
        if record['confirmedEpoch'] is not None: _lc_uint(record['confirmedEpoch'])
        token = record['stableToken']
        if token is not None:
            _fixture_fields(token, 'deathEpoch hero skin'); _lc_uint(token['deathEpoch']); _lc_string(token['hero']); _lc_string(token['skin'])
    return data


def load_lifecycle_fixture(path):
    try: root = _lc_json(Path(path).read_bytes())
    except OSError as error: raise FixtureFormatError('Cannot read lifecycle fixture') from error
    _fixture_fields(root, 'schemaVersion cases')
    _fixture_require(type(root['schemaVersion']) is int and root['schemaVersion'] == 1, 'Lifecycle schema version')
    cases = root['cases']; _fixture_require(type(cases) is list and bool(cases), 'No lifecycle cases')
    ids = set()
    for case in cases:
        _lc_case(case); _fixture_require(case['caseId'] not in ids, 'Duplicate lifecycle case ID'); ids.add(case['caseId'])
    return cases


def observe_lifecycle(state, signal):
    """Independent ordinary-value lifecycle transition table; never reads golden outputs."""
    _lc_state(state); _lc_signal(signal)
    next_state = dict(state)
    def result(diagnosis, epoch=None, token=None):
        return dict(state=next_state, diagnosis=diagnosis, confirmedEpoch=epoch, stableToken=token)
    hero, skin = state['currentHero'], state['currentSkin']
    armed, pending = state['armedOccurrence'], state['pendingEpoch']
    last, water, count = int(state['lastConfirmedEpoch']), int(state['occurrenceHighWater']), state['stableCount']
    invalid = ((hero is None) != (skin is None) or last > water or not 0 <= count <= 2 or
               (pending is None and count != 0) or (pending is not None and (int(pending) == 0 or int(pending) != last)) or
               ((state['armedHero'] is None) != (armed is None)) or ((state['armedSkin'] is None) != (armed is None)) or
               (hero is None and (water != 0 or armed is not None or pending is not None)))
    if armed is not None:
        invalid |= (state['armedHero'] != hero or state['armedSkin'] != skin or int(armed) <= last or int(armed) != water or pending is not None)
    if invalid: return result('invalid-state')
    same = signal['hero'] == hero and signal['skin'] == skin
    kind = signal['type']
    if kind == 'Rebind':
        if same: return result('same-binding')
        next_state.update(currentHero=signal['hero'], currentSkin=signal['skin'], stableCount=0, armedHero=None, armedSkin=None, armedOccurrence=None)
        return result('rebound')
    if hero is None: return result('unbound')
    if kind == 'Update':
        if not same: return result('stale-observation')
        if pending is None: return result('no-pending-epoch')
        stable = all(signal[k] for k in ('acceptingInput', 'fullDamageMode', 'canTakeDamage', 'playable')) and signal['health'] > 0 and not any(signal[k] for k in ('paused', 'cutscene', 'sceneTransition'))
        if not stable:
            next_state['stableCount'] = 0
            return result('unstable-observation')
        if count < 2:
            next_state['stableCount'] = count + 1
            return result('stabilizing')
        next_state.update(stableCount=0, pendingEpoch=None)
        return result('stable-respawn', token=dict(deathEpoch=pending, hero=hero, skin=skin))
    if not same: return result('stale-binding')
    occurrence = int(signal['occurrence'])
    if occurrence == 0: return result('invalid-occurrence')
    if kind == 'BeforeDeath':
        if occurrence <= water: return result('consumed-occurrence')
        if armed is not None: return result('candidate-already-armed')
        if pending is not None: return result('pending-epoch')
        next_state.update(armedHero=hero, armedSkin=skin, armedOccurrence=signal['occurrence'], occurrenceHighWater=signal['occurrence'])
        return result('armed')
    if armed is None: return result('consumed-occurrence' if occurrence <= water else 'unarmed-after-death')
    if signal['occurrence'] != armed: return result('mismatched-occurrence')
    # Valid armed occurrences are strictly newer than last, so exhaustion cannot reach confirmation.
    epoch = str(last + 1)
    next_state.update(armedHero=None, armedSkin=None, armedOccurrence=None, pendingEpoch=epoch, stableCount=0, lastConfirmedEpoch=epoch)
    return result('death-confirmed', epoch=epoch)


def project_lifecycle_decision(step, decision):
    return dict(step=step, state=dict(decision['state']), diagnosis=decision['diagnosis'], confirmedEpoch=decision['confirmedEpoch'], stableToken=None if decision['stableToken'] is None else dict(decision['stableToken']))


def verify_lifecycle_case(case):
    data = _lc_case(case); state = dict(data['initialState']); actual = []
    for i, signal in enumerate(data['events']):
        decision = observe_lifecycle(state, signal)
        actual.append(project_lifecycle_decision(i, decision)); state = decision['state']
    if actual != case['expected']:
        raise AssertionError(f"{case['caseId']}: lifecycle full step log mismatch: expected {case['expected']!r}, got {actual!r}")


# transaction-forward-v1: complete TYPE grammar, intentionally bounded forward reducer.
# 64 KiB and 1..32 events are HARNESS limits, not production constraints.
# Supplied VerifiedRegistryHead values do not confer disk/runtime authority.
TRANSACTION_CONTRACT = 'transaction-forward-v1'
_TX_PHASES = 'IDLE PREPARING PREPARED ARMED APPLIED ROLLBACK_PENDING ROLLED_BACK COMMITTED BLOCKED'.split()
_TX_SUPPORTED_PHASES = frozenset('IDLE PREPARING PREPARED ARMED APPLIED COMMITTED'.split())
_TX_SUPPORTED_EVENTS = frozenset('Begin Prepared ArmCommitted ApplyVerified CompletionCommitted'.split())
TRANSACTION_ROLLBACK_CONTRACT = 'transaction-rollback-success-v1'
_TX_ROLLBACK_PHASES = _TX_SUPPORTED_PHASES | {'ROLLBACK_PENDING', 'ROLLED_BACK'}
_TX_ROLLBACK_EVENTS = _TX_SUPPORTED_EVENTS | {'ApplyFailed', 'RollbackVerified'}
# Failure vocabulary admission is not an exhaustive phase/event coverage claim.
TRANSACTION_FAILURE_CONTRACT = 'transaction-failure-blocked-v1'
_TX_FAILURE_PHASES = _TX_ROLLBACK_PHASES | {'BLOCKED'}
_TX_FAILURE_EVENTS = _TX_ROLLBACK_EVENTS | {'RollbackFailed', 'CompletionRejected', 'CompletionIndeterminate'}


def _tx_scope(contract):
    if contract == TRANSACTION_CONTRACT: return _TX_SUPPORTED_PHASES, _TX_SUPPORTED_EVENTS
    if contract == TRANSACTION_ROLLBACK_CONTRACT: return _TX_ROLLBACK_PHASES, _TX_ROLLBACK_EVENTS
    if contract == TRANSACTION_FAILURE_CONTRACT: return _TX_FAILURE_PHASES, _TX_FAILURE_EVENTS
    raise OracleOutOfScope('Unknown transaction contract')
_TX_ENUMS = {'Phase': _TX_PHASES, 'Mode': ['OFF', 'ON', 'ROTATE'], 'Operation': list(OPERATIONS), 'LockState': ['CLEAR', 'ARMED', 'ROLLBACK_FAILED']}
_TX_TYPES = {
    'Binding': 'value:String',
    'Snapshot': 'mode:Mode selectedPackId:String? active:Visual skinStamp:Long',
    'Envelope': 'transactionId:String operation:Operation baseGenerationId:String baseGenerationSha256:String prior:Snapshot target:Snapshot binding:Binding priorEstablishedOnBinding:Bool',
    'Correlation': 'transactionId:String binding:Binding',
    'Receipt': 'expectedGenerationId:String expectedGenerationSha256:String newGenerationId:String newGenerationSha256:String',
    'Interlock': 'state:LockState transactionId:String? operation:Operation? baseGenerationId:String? baseGenerationSha256:String? prior:Snapshot? target:Snapshot? bindingToken:Binding? priorEstablishedOnBinding:Bool? originalFailure:String? rollbackFailure:String?',
    'Head': 'generationId:String generationSha256:String activation:Snapshot interlock:Interlock',
    'State': 'phase:Phase interlock:Interlock binding:Binding? armCommitReceipt:Receipt? pendingClosure:Snapshot? envelope:Envelope? activation:Snapshot? originalFailure:String? rollbackFailure:String? completionReceipt:Receipt? failureReceipt:Receipt?',
}
_TX_UNIONS = {
    'Visual': {'Vanilla': '', 'Pack': 'id:String treeSha256:String contentSha256:String importReceiptSha256:String'},
    'Event': {'Begin': 'envelope:Envelope', 'Prepared': 'correlation:Correlation', 'ArmCommitted': 'correlation:Correlation commitReceipt:Receipt verifiedHead:Head', 'ApplyVerified': 'correlation:Correlation', 'ApplyFailed': 'correlation:Correlation code:String', 'RollbackVerified': 'correlation:Correlation freshBinding:Bool', 'RollbackFailed': 'correlation:Correlation code:String persistedFailureReceipt:Receipt? verifiedHead:Head?', 'CompletionCommitted': 'correlation:Correlation commitReceipt:Receipt verifiedHead:Head', 'CompletionRejected': 'correlation:Correlation code:String', 'CompletionIndeterminate': 'correlation:Correlation'},
    'Command': {'Prepare': 'envelope:Envelope desired:Visual', 'Arm': 'envelope:Envelope', 'Apply': 'correlation:Correlation', 'Rollback': 'correlation:Correlation', 'Commit': 'correlation:Correlation expectedGenerationId:String expectedGenerationSha256:String closure:Snapshot'},
}


def _tx_type(value, kind):
    """Closed recursive representation grammar. No UUID/hash/token semantics here."""
    if kind.endswith('?'):
        if value is None: return
        return _tx_type(value, kind[:-1])
    if kind == 'String': return _lc_string(value)
    if kind == 'Bool': return _fixture_require(type(value) is bool, 'Transaction boolean required')
    if kind == 'Long':
        _lc_string(value)
        return _fixture_require(re.fullmatch(r'0|-?[1-9][0-9]*', value) is not None and len(value) <= 20 and -9223372036854775808 <= int(value) <= 9223372036854775807, 'Canonical signed Int64 string required')
    if kind in _TX_ENUMS:
        _lc_string(value); return _fixture_require(value in _TX_ENUMS[kind], 'Unknown transaction enum')
    if kind in _TX_UNIONS:
        _fixture_require(type(value) is dict and type(value.get('type')) is str and value['type'] in _TX_UNIONS[kind], 'Unknown transaction discriminant')
        fields = 'type:String ' + _TX_UNIONS[kind][value['type']]
    else: fields = _TX_TYPES[kind]
    members = [entry.split(':') for entry in fields.split()]
    _fixture_fields(value, ' '.join(k for k, _ in members))
    for key, child in members: _tx_type(value[key], child)


def _tx_case(case):
    _fixture_fields(case, 'caseId oracleContract input inputSha256 expected')
    _lc_string(case['caseId']); _fixture_require(bool(case['caseId']), 'Empty transaction case ID'); _lc_string(case['oracleContract'])
    source = case['input']
    _fixture_require(type(source) is dict and set(source) in ({'utf8Text'}, {'utf8Hex'}), 'Exactly one transaction encoding')
    value = next(iter(source.values())); _lc_string(value)
    if 'utf8Hex' in source:
        _fixture_require(re.fullmatch(r'(?:[0-9a-f]{2})*', value) is not None, 'Lowercase even hex required'); raw = bytes.fromhex(value)
    else: raw = value.encode('utf-8')
    _lc_string(case['inputSha256'])
    _fixture_require(HASH.fullmatch(case['inputSha256']) is not None and hashlib.sha256(raw).hexdigest() == case['inputSha256'], 'Raw transaction hash mismatch')
    data = _lc_json(raw); _fixture_fields(data, 'initialState events'); _tx_type(data['initialState'], 'State')
    events = data['events']; _fixture_require(type(events) is list and bool(events), 'Missing transaction events')
    for event in events: _tx_type(event, 'Event')
    rows = case['expected']; _fixture_require(type(rows) is list and len(rows) == len(events), 'Full transaction rows required')
    for i, row in enumerate(rows):
        _fixture_fields(row, 'step inputStateValid stateValid state diagnosis commands')
        _lc_int(row['step']); _fixture_require(row['step'] == i, 'Transaction step order')
        _tx_type(row['inputStateValid'], 'Bool'); _tx_type(row['stateValid'], 'Bool'); _tx_type(row['state'], 'State'); _tx_type(row['diagnosis'], 'String')
        _fixture_require(type(row['commands']) is list, 'Ordered command array required')
        for command in row['commands']: _tx_type(command, 'Command')
    # Classify only after COMPLETE recursive parsing, before any semantic validator/reducer.
    phases, supported_events = _tx_scope(case['oracleContract'])
    if len(raw) > 65536 or len(events) > 32 or data['initialState']['phase'] not in phases or any(e['type'] not in supported_events for e in events):
        raise OracleOutOfScope('Transaction contract phase/event/64KiB/32-event harness boundary')
    return data


def load_transaction_fixture(path):
    try: root = _lc_json(Path(path).read_bytes())
    except FileNotFoundError as error: raise FixtureFormatError('Missing transaction corpus') from error
    _fixture_fields(root, 'schemaVersion cases'); _fixture_require(type(root['schemaVersion']) is int and root['schemaVersion'] == 1, 'Transaction schema version')
    _fixture_require(type(root['cases']) is list and bool(root['cases']), 'No transaction cases')
    ids = set()
    for case in root['cases']:
        _tx_case(case); _fixture_require(case['caseId'] not in ids, 'Duplicate transaction case ID'); ids.add(case['caseId'])
    return root['cases']


def _tx_match(pattern, text):
    return type(text) is str and re.fullmatch(pattern, text) is not None


def _tx_token(binding):
    if binding is None: return False
    text = binding['value']
    return (1 <= len(text) <= 256 and ord(text[0]) not in _TRIM_SPACE and ord(text[-1]) not in _TRIM_SPACE and
            not any(ord(c) < 32 or 127 <= ord(c) <= 159 or ord(c) in _BIDI for c in text) and unicodedata.normalize('NFKC', text) == text)


def _tx_snapshot(a):
    if a is None or int(a['skinStamp']) < 0: return False
    if a['selectedPackId'] is not None and not _tx_match(PACK_ID, a['selectedPackId']): return False
    v = a['active']
    return v['type'] == 'Vanilla' or (_tx_match(PACK_ID, v['id']) and all(_tx_match(HASH, v[k]) for k in IDENTITY))


def _tx_same_visual(a, b):
    # Import receipt identity is CAS data, not a visual change.
    return {k: v for k, v in a.items() if k != 'importReceiptSha256'} == {k: v for k, v in b.items() if k != 'importReceiptSha256'}


def _tx_envelope(e):
    if e is None: return False
    p, t = e['prior'], e['target']
    if not (_tx_match(UUID, e['transactionId']) and _tx_match(UUID, e['baseGenerationId']) and _tx_match(HASH, e['baseGenerationSha256']) and _tx_token(e['binding']) and _tx_snapshot(p) and _tx_snapshot(t)): return False
    if int(p['skinStamp']) == 9223372036854775807 or int(t['skinStamp']) != int(p['skinStamp']) + 1: return False
    same = _tx_same_visual(p['active'], t['active'])
    if same and e['priorEstablishedOnBinding']: return False
    selected = t['active']['type'] == 'Pack' and t['selectedPackId'] == t['active']['id']
    retained_selection = p['selectedPackId'] == t['selectedPackId']
    rules = {
        'MODE_ON': p['mode'] == 'OFF' and t['mode'] == 'ON' and retained_selection and selected,
        'MODE_OFF': p['mode'] == 'ROTATE' and t['mode'] == 'OFF' and retained_selection and t['active']['type'] == 'Vanilla',
        'DEATH_ROTATION': p['mode'] == t['mode'] == 'ROTATE' and selected and not same,
    }
    if e['operation'] in rules: return rules[e['operation']]
    return not e['priorEstablishedOnBinding'] and p['mode'] == t['mode'] and retained_selection and ((t['mode'] == 'ON' and selected) or (t['mode'] == 'ROTATE' and t['active']['type'] == 'Pack' and t['active'] == p['active']))


def _tx_clear():
    return dict(state='CLEAR', transactionId=None, operation=None, baseGenerationId=None, baseGenerationSha256=None, prior=None, target=None, bindingToken=None, priorEstablishedOnBinding=None, originalFailure=None, rollbackFailure=None)


def _tx_armed(e):
    return dict(state='ARMED', transactionId=e['transactionId'], operation=e['operation'], baseGenerationId=e['baseGenerationId'], baseGenerationSha256=e['baseGenerationSha256'], prior=e['prior'], target=e['target'], bindingToken=e['binding'], priorEstablishedOnBinding=e['priorEstablishedOnBinding'], originalFailure=None, rollbackFailure=None)


def _tx_receipt(r, generation, digest):
    return r is not None and all(_tx_match(UUID if k.endswith('Id') else HASH, v) for k, v in r.items()) and r['expectedGenerationId'] == generation and r['expectedGenerationSha256'] == digest and r['newGenerationId'] != generation and r['newGenerationSha256'] != digest


def _tx_follows(s, r):
    arm, e = s['armCommitReceipt'], s['envelope']
    return arm is not None and _tx_receipt(r, arm['newGenerationId'], arm['newGenerationSha256']) and r['newGenerationId'] != e['baseGenerationId'] and r['newGenerationSha256'] != e['baseGenerationSha256']


def _tx_head(h, r, a, lock):
    return h is not None and r is not None and h == dict(generationId=r['newGenerationId'], generationSha256=r['newGenerationSha256'], activation=a, interlock=lock)


def _tx_prior_closure(e):
    # Only unestablished historical Pack restoration consumes a new stamp.
    return dict(e['prior'], skinStamp=str(int(e['prior']['skinStamp']) + (not e['priorEstablishedOnBinding'] and e['prior']['active']['type'] == 'Pack')))


def transaction_state_valid(s, *, contract=TRANSACTION_CONTRACT):
    """Public-validator observations for supported phases, not reachability claims."""
    _tx_type(s, 'State')
    phases, _ = _tx_scope(contract)
    if s['phase'] not in phases: raise OracleOutOfScope('Excluded transaction phase')
    if not _tx_token(s['binding']) or not _tx_snapshot(s['activation']): return False
    if any(s[k] is not None and not _tx_match(FAILURE_CODE, s[k]) for k in ('originalFailure', 'rollbackFailure')): return False
    null_fields = ('armCommitReceipt', 'pendingClosure', 'originalFailure', 'rollbackFailure', 'completionReceipt', 'failureReceipt')
    if s['phase'] == 'IDLE': return s['interlock'] == _tx_clear() and s['envelope'] is None and all(s[k] is None for k in null_fields)
    e = s['envelope']
    if not _tx_envelope(e) or e['binding'] != s['binding']: return False
    if s['phase'] in ('PREPARING', 'PREPARED'): return s['interlock'] == _tx_clear() and s['activation'] == e['prior'] and all(s[k] is None for k in null_fields)
    if not _tx_receipt(s['armCommitReceipt'], e['baseGenerationId'], e['baseGenerationSha256']): return False
    if s['phase'] == 'COMMITTED':
        closure = e['target'] if s['originalFailure'] is None else _tx_prior_closure(e)
        return s['interlock'] == _tx_clear() and s['pendingClosure'] == closure and s['activation'] == closure and _tx_follows(s, s['completionReceipt']) and s['failureReceipt'] is None and s['rollbackFailure'] is None
    if s['phase'] == 'BLOCKED':
        if s['activation'] != e['prior'] or s['completionReceipt'] is not None: return False
        closure = e['target'] if s['originalFailure'] is None else _tx_prior_closure(e)
        if s['pendingClosure'] is not None and s['pendingClosure'] != closure: return False
        if s['pendingClosure'] is None and (s['originalFailure'] is None or s['rollbackFailure'] is None): return False
        if s['failureReceipt'] is None: return s['interlock'] == _tx_armed(e)
        failed_lock = dict(_tx_armed(e), state='ROLLBACK_FAILED', originalFailure=s['originalFailure'], rollbackFailure=s['rollbackFailure'])
        return (s['pendingClosure'] is None and s['originalFailure'] is not None and s['rollbackFailure'] is not None and
                _tx_follows(s, s['failureReceipt']) and s['interlock'] == failed_lock)
    if not (s['activation'] == e['prior'] and s['completionReceipt'] is None and s['interlock'] == _tx_armed(e) and s['failureReceipt'] is None and s['rollbackFailure'] is None): return False
    if s['phase'] in ('ROLLBACK_PENDING', 'ROLLED_BACK'):
        return s['originalFailure'] is not None and s['pendingClosure'] == (_tx_prior_closure(e) if s['phase'] == 'ROLLED_BACK' else None)
    return s['originalFailure'] is None and s['pendingClosure'] == (e['target'] if s['phase'] == 'APPLIED' else None)


def decide_transaction(state, event, *, contract=TRANSACTION_CONTRACT):
    """Independent value table; versioned transaction scope, no production source imports."""
    import copy
    _tx_type(state, 'State'); _tx_type(event, 'Event')
    phases, events = _tx_scope(contract)
    if state['phase'] not in phases or event['type'] not in events: raise OracleOutOfScope('Excluded transaction phase/event')
    s = copy.deepcopy(state)
    def result(diagnosis, commands=(), **changes):
        s.update(changes); return dict(state=s, diagnosis=diagnosis, commands=copy.deepcopy(list(commands)))
    if not transaction_state_valid(s, contract=contract): return result('invalid-state')
    phase, kind = s['phase'], event['type']
    if phase in ('COMMITTED', 'BLOCKED'): return result('terminal')
    if kind == 'Begin':
        if phase != 'IDLE': return result('transaction-in-progress')
        e = event['envelope']
        if not _tx_envelope(e) or e['binding'] != s['binding'] or e['prior'] != s['activation']: return result('invalid-envelope')
        return result('prepare', [dict(type='Prepare', envelope=e, desired=e['target']['active'])], phase='PREPARING', envelope=copy.deepcopy(e))
    c, e = event['correlation'], s['envelope']
    if e is None or not _tx_match(UUID, c['transactionId']) or not _tx_token(c['binding']) or c['transactionId'] != e['transactionId'] or c['binding'] != s['binding']: return result('stale-correlation')
    if (phase, kind) == ('PREPARING', 'Prepared'): return result('arm', [dict(type='Arm', envelope=e)], phase='PREPARED')
    if (phase, kind) == ('PREPARED', 'ArmCommitted'):
        r = event['commitReceipt']; lock = _tx_armed(e)
        if not _tx_receipt(r, e['baseGenerationId'], e['baseGenerationSha256']) or not _tx_head(event['verifiedHead'], r, e['prior'], lock): return result('stale-receipt')
        return result('apply', [dict(type='Apply', correlation=c)], phase='ARMED', interlock=lock, armCommitReceipt=copy.deepcopy(r))
    if (phase, kind) == ('ARMED', 'ApplyVerified'):
        arm = s['armCommitReceipt']
        return result('commit-closure', [dict(type='Commit', correlation=c, expectedGenerationId=arm['newGenerationId'], expectedGenerationSha256=arm['newGenerationSha256'], closure=e['target'])], phase='APPLIED', pendingClosure=copy.deepcopy(e['target']))
    if (phase, kind) == ('ARMED', 'ApplyFailed'):
        if not _tx_match(FAILURE_CODE, event['code']): return result('invalid-code')
        return result('rollback', [dict(type='Rollback', correlation=c)], phase='ROLLBACK_PENDING', pendingClosure=None, originalFailure=event['code'])
    if (phase, kind) == ('ROLLBACK_PENDING', 'RollbackVerified'):
        if event['freshBinding'] == e['priorEstablishedOnBinding']: return result('invalid-binding-proof')
        closure = _tx_prior_closure(e); arm = s['armCommitReceipt']
        return result('commit-closure', [dict(type='Commit', correlation=c, expectedGenerationId=arm['newGenerationId'], expectedGenerationSha256=arm['newGenerationSha256'], closure=closure)], phase='ROLLED_BACK', pendingClosure=closure)
    if (phase, kind) == ('ROLLBACK_PENDING', 'RollbackFailed'):
        if not _tx_match(FAILURE_CODE, event['code']): return result('invalid-code')
        failed_lock = dict(s['interlock'], state='ROLLBACK_FAILED', originalFailure=s['originalFailure'], rollbackFailure=event['code'])
        receipt = event['persistedFailureReceipt']
        persisted = _tx_follows(s, receipt) and _tx_head(event['verifiedHead'], receipt, e['prior'], failed_lock)
        return result('rollback-failed', phase='BLOCKED', rollbackFailure=event['code'],
                      interlock=failed_lock if persisted else s['interlock'], failureReceipt=copy.deepcopy(receipt) if persisted else None)
    if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionRejected':
        if not _tx_match(FAILURE_CODE, event['code']): return result('invalid-code')
        if phase == 'APPLIED':
            return result('rollback', [dict(type='Rollback', correlation=c)], phase='ROLLBACK_PENDING', pendingClosure=None, originalFailure=event['code'])
        return result('rollback-closure-rejected', phase='BLOCKED', rollbackFailure=event['code'])
    if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionIndeterminate':
        return result('completion-indeterminate', phase='BLOCKED')
    if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionCommitted':
        r = event['commitReceipt']
        if not _tx_follows(s, r) or not _tx_head(event['verifiedHead'], r, s['pendingClosure'], _tx_clear()): return result('stale-receipt')
        return result('committed', phase='COMMITTED', interlock=_tx_clear(), activation=copy.deepcopy(s['pendingClosure']), completionReceipt=copy.deepcopy(r))
    return result('stale-phase')


def project_transaction_decision(step, input_valid, decision, *, contract=TRANSACTION_CONTRACT):
    import copy
    return dict(step=step, inputStateValid=input_valid, stateValid=transaction_state_valid(decision['state'], contract=contract), state=copy.deepcopy(decision['state']), diagnosis=decision['diagnosis'], commands=copy.deepcopy(decision['commands']))


def verify_transaction_case(case):
    data = _tx_case(case); state = data['initialState']; actual = []
    # Preserve the accepted default call seams (including two-argument substitutions).
    options = {} if case['oracleContract'] == TRANSACTION_CONTRACT else {'contract': case['oracleContract']}
    for i, event in enumerate(data['events']):
        before = transaction_state_valid(state, **options); decision = decide_transaction(state, event, **options)
        actual.append(project_transaction_decision(i, before, decision, **options)); state = decision['state']
    if actual != case['expected']: raise AssertionError(case['caseId'] + ': full ordered transaction log mismatch')
