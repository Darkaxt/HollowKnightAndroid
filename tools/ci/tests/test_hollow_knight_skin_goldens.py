import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
FIXTURE = ROOT / "tools/skin-goldens/v1/launch-descriptors.json"
spec = importlib.util.spec_from_file_location("skin_golden_oracle", ROOT / "tools/skins/skin_golden_oracle.py")
oracle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(oracle)


class HollowKnightSkinGoldensTest(unittest.TestCase):
    def test_original41_semantic_snapshot_and_new_matrix_bounds(self):
        cases = oracle.load_fixture(FIXTURE)
        original = [c for c in cases if c['oracleContract'] == oracle.CONTRACT]
        self.assertEqual(41, len(original))
        snapshot = json.dumps(original, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()
        self.assertEqual('74aa435248321797ea96298b5bf1184ee85628e58a813b344f620569011c2a26', hashlib.sha256(snapshot).hexdigest())
        original87 = json.dumps(cases[:87], sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()
        self.assertEqual('c60a75b98beddd3c73258a1e86724240161f9e20d366b468fb549f9988a405ef', hashlib.sha256(original87).hexdigest())
        self.assertEqual(174, len(cases))
        self.assertEqual(28, sum(c['expected']['outcome'] == 'OK' for c in cases))
        for case in cases[41:]:
            raw = case['input']['utf8Text'].encode('utf-8')
            self.assertLessEqual(len(raw), 65536)
            self.assertEqual(case['inputSha256'], case['expectedSha256'])

    def test_clear_catalog_is_pinned_and_infrastructure_errors_escape(self):
        raw = (ROOT / 'docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt').read_bytes()
        self.assertEqual(205, len(oracle._catalog_bytes(raw)))
        for bad in [raw[:-1], raw + b'\n', b'\xef\xbb\xbf' + raw, raw.replace(b'\n', b'\r\n'), raw.replace(b'Knight.png', b'Sprint.png'), b'\xff' + raw[1:]]:
            with self.assertRaises(oracle.FixtureFormatError):
                oracle._catalog_bytes(bad)
        case = next(c for c in oracle.load_fixture(FIXTURE) if c['caseId'] == 'clear-selected-only')
        with patch.object(oracle, '_catalog', side_effect=oracle.FixtureFormatError('catalog corrupt')):
            with self.assertRaises(oracle.FixtureFormatError):
                oracle.verify_case(case)
        oracle._catalog.cache_clear()
        try:
            with patch.object(Path, 'read_bytes', side_effect=OSError('missing catalog')):
                with self.assertRaises(oracle.FixtureFormatError):
                    oracle._catalog()
        finally:
            oracle._catalog.cache_clear()

    def test_clear_nonclear_and_large_inputs_remain_out_of_scope(self):
        case = next(c for c in oracle.load_fixture(FIXTURE) if c['caseId'] == 'clear-selected-only')
        for raw in [case['input']['utf8Text'].replace('"CLEAR"', '"ARMED"').encode(), case['input']['utf8Text'].replace('"CLEAR"', '"ROLLBACK_FAILED"').encode(), b' ' * 65537]:
            with self.assertRaises(oracle.OracleOutOfScope):
                oracle.parse_launch_descriptor(raw, hashlib.sha256(raw).hexdigest(), case['expectations'], contract=oracle.CLEAR_CONTRACT)

    def test_every_pack_object_texture_semantic_field_and_order_is_observed(self):
        case = next(c for c in oracle.load_fixture(FIXTURE) if c['caseId'] == 'clear-receipt-only-retained')
        records = case['expected']['semanticLog']
        for i, record in enumerate(records):
            for j, value in enumerate(record):
                bad = copy.deepcopy(case)
                bad['expected']['semanticLog'][i][j] = not value if type(value) is bool else value + 1 if type(value) is int else 'damaged'
                with self.subTest(record=i, field=j):
                    with self.assertRaises((oracle.FixtureFormatError, AssertionError)):
                        oracle.verify_case(bad)
        for index in range(len(records)):
            bad = copy.deepcopy(case); del bad['expected']['semanticLog'][index]
            with self.assertRaises((oracle.FixtureFormatError, AssertionError)):
                oracle.verify_case(bad)
        bad = copy.deepcopy(case)
        bad['expected']['semanticLog'][6], bad['expected']['semanticLog'][9] = bad['expected']['semanticLog'][9], bad['expected']['semanticLog'][6]
        with self.assertRaises((oracle.FixtureFormatError, AssertionError)):
            oracle.verify_case(bad)

    def test_interlock_semantic_fields_records_outcomes_and_scope(self):
        cases = oracle.load_fixture(FIXTURE)
        for name in ('interlock-armed-distinct', 'interlock-rollback-distinct', 'interlock-selected-not-active', 'interlock-repeated-receipt-retained'):
            case = next(c for c in cases if c['caseId'] == name)
            records = case['expected']['semanticLog']
            for i, row in enumerate(records):
                for j, value in enumerate(row):
                    bad = copy.deepcopy(case)
                    bad['expected']['semanticLog'][i][j] = not value if type(value) is bool else value + 1 if type(value) is int else 'damaged'
                    with self.subTest(case=name, row=i, field=j):
                        with self.assertRaises((oracle.FixtureFormatError, AssertionError)):
                            oracle.verify_case(bad)
                for change in ('omit', 'extra', 'reorder'):
                    bad = copy.deepcopy(case); log = bad['expected']['semanticLog']
                    if change == 'omit': del log[i]
                    elif change == 'extra': log[i].append('unknown')
                    else: log[i], log[(i + 1) % len(log)] = log[(i + 1) % len(log)], log[i]
                    with self.assertRaises((oracle.FixtureFormatError, AssertionError)):
                        oracle.verify_case(bad)
            bad = copy.deepcopy(case); bad['expected'] = {'outcome': 'DOCUMENT_INVALID', 'violation': 'FAKE'}
            with self.assertRaises(AssertionError): oracle.verify_case(bad)
            with patch.object(oracle, 'parse_launch_descriptor', return_value={'outcome': 'DOCUMENT_INVALID', 'violation': 'FAKE'}):
                with self.assertRaises(AssertionError): oracle.verify_case(case)
            raw = case['input']['utf8Text'].encode()
            for contract in (oracle.CONTRACT, oracle.CLEAR_CONTRACT):
                with self.assertRaises(oracle.OracleOutOfScope): oracle.parse_launch_descriptor(raw, case['expectedSha256'], case['expectations'], contract=contract)
        for contract in (oracle.CONTRACT, oracle.CLEAR_CONTRACT, oracle.INTERLOCK_CONTRACT):
            with self.assertRaises(oracle.OracleOutOfScope): oracle.parse_launch_descriptor(b' ' * 65537, '0' * 64, {}, contract=contract)
        with patch.object(oracle, '_catalog', side_effect=oracle.FixtureFormatError('catalog corrupt')):
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_case(case)

    def test_prior_selection_and_visual_id_negatives_isolate_equality(self):
        cases = oracle.load_fixture(FIXTURE)
        require = oracle._require
        for name, field in [('interlock-outer-prior-selected', 'selectedPackId'), ('interlock-outer-prior-visual-id', 'active')]:
            case = next(c for c in cases if c['caseId'] == name)
            value = json.loads(case['input']['utf8Text'])
            outer = {k: v for k, v in value['activation'].items() if k != 'rotationInterlock'}
            prior = value['activation']['rotationInterlock']['prior']
            self.assertEqual([field], [k for k in outer.keys() | prior.keys() if outer.get(k) != prior.get(k)])
            if field == 'active':
                self.assertEqual(['id'], [k for k in outer['active'].keys() | prior['active'].keys() if outer['active'].get(k) != prior['active'].get(k)])
            oracle.verify_case(case)
            # Exactly one equality component differs. Omitting its equality guard
            # must change the OUTCOME, not merely the independent violation label.
            def omit_equality(condition, code):
                if code != 'PRIOR_MISMATCH':
                    require(condition, code)
            with patch.object(oracle, '_require', side_effect=omit_equality):
                actual = oracle.parse_launch_descriptor(case['input']['utf8Text'].encode(), case['expectedSha256'], case['expectations'], contract=case['oracleContract'])
            self.assertEqual('OK', actual['outcome'], (name, actual))
            repaired = copy.deepcopy(value)
            if field == 'active':
                repaired['activation']['active']['id'] = prior['active']['id']
            else:
                repaired['activation'][field] = prior[field]
            raw = json.dumps(repaired, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()
            actual = oracle.parse_launch_descriptor(raw, hashlib.sha256(raw).hexdigest(), case['expectations'], contract=case['oracleContract'])
            self.assertEqual('OK', actual['outcome'], (name, actual))

    def test_interlock_log_shape_rejected_before_parser(self):
        case = next(c for c in oracle.load_fixture(FIXTURE) if c['caseId'] == 'interlock-rollback-distinct')
        for row, column, value in [(5, 1, 'bad-uuid'), (5, 2, 'UNKNOWN'), (5, 4, 'bad-hash'), (6, 4, 91), (8, 4, '092'), (10, 2, 0), (10, 1, ' x'), (11, 1, 'lower'), (11, 2, 'A' * 129)]:
            bad = copy.deepcopy(case); bad['expected']['semanticLog'][row][column] = value
            with patch.object(oracle, 'parse_launch_descriptor') as parse:
                with self.assertRaises(oracle.FixtureFormatError): oracle.verify_case(bad)
                parse.assert_not_called()

    def test_closed_clear_structures_and_display_guards(self):
        case = next(c for c in oracle.load_fixture(FIXTURE) if c['caseId'] == 'clear-selected-only')
        value = json.loads(case['input']['utf8Text'])
        # Metamorphic harness damage only; materialized fixture remains read-only.
        for level in ('pack', 'object', 'texture', 'visual'):
            for damage in ('unknown', 'missing', 'null'):
                bad = copy.deepcopy(value)
                part = bad['packs'][0]
                if level == 'object': part = part['currentObject']
                if level == 'texture': part = part['currentObject']['textures'][0]
                if level == 'visual': part = bad['activation']['active']
                field = next(iter(part))
                if damage == 'unknown': part['unused'] = 1
                elif damage == 'missing': del part[field]
                else: part[field] = None
                raw = json.dumps(bad, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()
                result = oracle.parse_launch_descriptor(raw, hashlib.sha256(raw).hexdigest(), case['expectations'], contract=oracle.CLEAR_CONTRACT)
                self.assertEqual('DOCUMENT_INVALID', result['outcome'], (level, damage))
        for display in [' A', 'A ', 'A\\B', 'AB', 'A‮B', '']:
            with self.assertRaises(oracle._Invalid): oracle._display(display)
        self.assertNotIn(0x85, oracle._TRIM_SPACE)
        self.assertIn(0x2007, oracle._TRIM_SPACE)

    def test_semantic_decimal_shape_is_checked_before_parser(self):
        first = json.loads(FIXTURE.read_text(encoding='utf-8'))['cases'][0]
        for row, column in [(0, 3), (1, 3)]:
            bad = copy.deepcopy(first)
            bad['expected']['semanticLog'][row][column] = 'not-a-decimal'
            with patch.object(oracle, 'parse_launch_descriptor') as parse:
                with self.assertRaises(oracle.FixtureFormatError):
                    oracle.verify_case(bad)
                parse.assert_not_called()

    def test_pinned_cases(self):
        cases = oracle.load_fixture(FIXTURE)
        self.assertGreaterEqual(len(cases), 25)
        self.assertGreaterEqual(sum(c['expected']['outcome'] == 'OK' for c in cases), 5)
        self.assertTrue(any(c['expected']['outcome'] == 'DOCUMENT_INVALID' for c in cases))
        for case in cases:
            with self.subTest(case=case['caseId']):
                oracle.verify_case(case)

    def test_positive_cannot_be_blanket_rejected(self):
        case = json.loads(FIXTURE.read_text(encoding='utf-8'))['cases'][0]
        raw = case['input']['utf8Text'].encode('utf-8')
        self.assertEqual('OK', oracle.parse_launch_descriptor(raw, case['expectedSha256'], case['expectations'], contract=case['oracleContract'])['outcome'])
        with patch.object(oracle, 'parse_launch_descriptor', return_value={'outcome': 'DOCUMENT_INVALID', 'violation': 'FAKE'}):
            with self.assertRaises(AssertionError):
                oracle.verify_case(case)

    def test_fixture_mutations_are_rejected_without_rewriting(self):
        source = json.loads(FIXTURE.read_text(encoding='utf-8'))
        mutations = [dict(source, cases=[]), {'schemaVersion': 1}, dict(source, unused=True), dict(source, schemaVersion=True)]
        duplicate = copy.deepcopy(source); duplicate['cases'].append(duplicate['cases'][0]); mutations.append(duplicate)
        for field, value in [('oracleContract', 'future'), ('unused', 0), ('expected', {'outcome': 'OK', 'semanticLog': []}), ('input', {'utf8Text': 'changed'}), ('expectations', {})]:
            bad = copy.deepcopy(source); bad['cases'][0][field] = value; mutations.append(bad)
        for bad in mutations:
            with self.subTest(bad=bad['cases'][0]['caseId'] if bad.get('cases') else 'outer'):
                with patch.object(Path, 'read_bytes', return_value=json.dumps(bad).encode()):
                    with self.assertRaises(oracle.FixtureFormatError):
                        oracle.load_fixture(FIXTURE)
        with patch.object(Path, 'read_bytes', return_value=b'{"schemaVersion":1,"schemaVersion":1,"cases":[]}'):
            with self.assertRaises(oracle.FixtureFormatError):
                oracle.load_fixture(FIXTURE)

    def test_wrong_outcome_and_damaged_semantic_value_fail(self):
        case = json.loads(FIXTURE.read_text(encoding='utf-8'))['cases'][0]
        wrong = copy.deepcopy(case); wrong['expected'] = {'outcome': 'DOCUMENT_INVALID', 'violation': 'FAKE'}
        damaged = copy.deepcopy(case); damaged['expected']['semanticLog'][0][9] = '1' * 64
        for bad in [wrong, damaged]:
            with self.assertRaises(AssertionError):
                oracle.verify_case(bad)

    def test_corrupted_bytes_are_checked_before_descriptor_hash(self):
        case = json.loads(FIXTURE.read_text(encoding='utf-8'))['cases'][0]
        case['input']['utf8Text'] += '\n'
        with self.assertRaises(oracle.FixtureFormatError):
            oracle.verify_case(case)

    def test_explicit_scope_boundaries(self):
        case = json.loads(FIXTURE.read_text(encoding='utf-8'))['cases'][0]
        text = case['input']['utf8Text']
        for raw in [text.replace('"packs":[]', '"packs":[{}]').encode(), text.replace('"CLEAR"', '"ARMED"').encode(), text.replace('"CLEAR"', '"ROLLBACK_FAILED"').encode(), b' ' * 65537]:
            with self.assertRaises(oracle.OracleOutOfScope):
                oracle.parse_launch_descriptor(raw, hashlib.sha256(raw).hexdigest(), case['expectations'], contract=case['oracleContract'])
        with self.assertRaises(oracle.OracleOutOfScope):
            oracle.parse_launch_descriptor(b'{}', '0' * 64, {}, contract='full-wire')

    def test_lexical_and_structural_guards(self):
        case = json.loads(FIXTURE.read_text(encoding='utf-8'))['cases'][0]
        for raw, violation in [(b'[' * 1100 + b']' * 1100, 'STRUCTURE_LIMIT'), (b'[' + b'0,' * 256 + b'0]', 'STRUCTURE_LIMIT'), (b'{"x":NaN}', 'JSON_SYNTAX'), (b'{"x":Infinity}', 'JSON_SYNTAX'), (b'{"x":"\\ud800"}', 'JSON_SYNTAX')]:
            result = oracle.parse_launch_descriptor(raw, hashlib.sha256(raw).hexdigest(), case['expectations'], contract=case['oracleContract'])
            self.assertEqual({'outcome': 'DOCUMENT_INVALID', 'violation': violation}, result)

    def test_independent_canonical_unicode_and_controls(self):
        # Lexical helper checks only; these values do not extend the descriptor subset.
        value = {'': '\x00\b\t\n\f\r\\"', '\U00010000': '\U0001f600'}
        self.assertEqual('{"\U00010000":"\U0001f600","":"\\u0000\\b\\t\\n\\f\\r\\\\\\\""}', oracle._canonical(value))


class HollowKnightSkinLifecycleGoldensTest(unittest.TestCase):
    fixture = ROOT / 'tools/skin-goldens/v1/lifecycle.json'

    def test_corpus_exact_bytes_full_logs_positive_and_noop(self):
        cases = oracle.load_lifecycle_fixture(self.fixture)
        self.assertEqual(36, len(cases))
        self.assertEqual(152, sum(len(c['expected']) for c in cases))
        for case in cases:
            with self.subTest(case=case['caseId']): oracle.verify_lifecycle_case(case)
        self.assertTrue(any(r['stableToken'] is not None for c in cases for r in c['expected']))
        self.assertTrue(any(r['diagnosis'] == 'same-binding' for c in cases for r in c['expected']))
        # Guard-isolation regression: mutate only a trusted Python oracle copy in memory.
        # Never edit/extract/execute a Kotlin/C# core or change the oracle source file.
        oracle_path = ROOT / 'tools/skins/skin_golden_oracle.py'
        source_bytes = oracle_path.read_bytes()
        source = source_bytes.decode('utf-8')
        for case_id, guard, omitted in (
            ('invalid-pending-zero', 'int(pending) == 0 or int(pending) != last', 'int(pending) != last'),
            ('invalid-armed-and-pending', 'int(armed) != water or pending is not None', 'int(armed) != water'),
        ):
            with self.subTest(isolated_guard=case_id):
                self.assertEqual(1, source.count(guard))
                namespace = {'__file__': str(oracle_path), '__name__': 'lifecycle_guard_probe'}
                exec(compile(source.replace(guard, omitted), '<in-memory-lifecycle-guard-probe>', 'exec'), namespace)
                case = next(c for c in cases if c['caseId'] == case_id)
                data = json.loads(case['input']['utf8Text']); state = data['initialState']; signal = data['events'][0]
                self.assertEqual('invalid-state', oracle.observe_lifecycle(state, signal)['diagnosis'])
                self.assertEqual('rebound', namespace['observe_lifecycle'](state, signal)['diagnosis'])
                with self.assertRaises(AssertionError): namespace['verify_lifecycle_case'](case)
                # Removing this one pending field repairs the isolated invalid snapshot.
                self.assertEqual('rebound', oracle.observe_lifecycle(dict(state, pendingEpoch=None), signal)['diagnosis'])
        self.assertEqual(source_bytes, oracle_path.read_bytes())

    def test_every_projected_field_and_record_order_is_observed(self):
        cases = oracle.load_lifecycle_fixture(self.fixture)
        def leaves(value, path=()):
            if isinstance(value, dict):
                for k, v in value.items(): yield from leaves(v, path + (k,))
            else: yield path, value
        for case in cases[:1]:
            for i, record in enumerate(case['expected']):
                for path, value in leaves(record):
                    bad = copy.deepcopy(case); part = bad['expected'][i]
                    for key in path[:-1]: part = part[key]
                    key = path[-1]
                    if key == 'stableToken': changed = {'deathEpoch': '7', 'hero': 'damaged', 'skin': 'damaged'}
                    elif key == 'diagnosis': changed = 'armed' if value != 'armed' else 'same-binding'
                    elif key in ('pendingEpoch', 'lastConfirmedEpoch', 'armedOccurrence', 'occurrenceHighWater', 'confirmedEpoch', 'deathEpoch'): changed = '7' if value != '7' else '8'
                    else: changed = value + 1 if type(value) is int else 'damaged'
                    part[key] = changed
                    # Shape-valid damage must fail the actual full-log assertion, not just validation.
                    with self.assertRaises(oracle.FixtureFormatError if key == 'step' else AssertionError):
                        oracle.verify_lifecycle_case(bad)
                for change in ('omit', 'extra', 'reorder'):
                    bad = copy.deepcopy(case); log = bad['expected']
                    if change == 'omit': del log[i]
                    elif change == 'extra': log[i]['unknown'] = 1
                    else: log[i], log[(i + 1) % len(log)] = log[(i + 1) % len(log)], log[i]
                    with self.assertRaises((oracle.FixtureFormatError, AssertionError)): oracle.verify_lifecycle_case(bad)
            bad = copy.deepcopy(case)
            bad['expected'][3], bad['expected'][5] = bad['expected'][5], bad['expected'][3]
            for i, record in enumerate(bad['expected']): record['step'] = i
            with self.assertRaises(AssertionError): oracle.verify_lifecycle_case(bad)

    def test_all_noop_and_all_invalid_substitutions_kill_positive(self):
        case = oracle.load_lifecycle_fixture(self.fixture)[0]
        for diagnosis in ('same-binding', 'invalid-state'):
            with patch.object(oracle, 'observe_lifecycle', side_effect=lambda state, signal: dict(state=copy.deepcopy(state), diagnosis=diagnosis, confirmedEpoch=None, stableToken=None)):
                with self.assertRaises(AssertionError): oracle.verify_lifecycle_case(case)

    def test_malformed_fixture_rejected_before_reducer(self):
        root = json.loads(self.fixture.read_text(encoding='utf-8'))
        mutations = [dict(root, cases=[]), dict(root, schemaVersion=True), dict(root, unused=0), {'cases': []}]
        duplicate = copy.deepcopy(root); duplicate['cases'].append(duplicate['cases'][0]); mutations.append(duplicate)
        for key, value in [('inputSha256', '0' * 64), ('unused', True), ('expected', []), ('input', {'utf8Text': '{}', 'utf8Hex': '00'})]:
            bad = copy.deepcopy(root); bad['cases'][0][key] = value; mutations.append(bad)
        for field, value in [('stableCount', True), ('stableCount', 2147483648), ('lastConfirmedEpoch', '01'), ('lastConfirmedEpoch', '18446744073709551616'), ('lastConfirmedEpoch', 1), ('lastConfirmedEpoch', None), ('currentHero', 3), ('unknown', 0)]:
            bad = copy.deepcopy(root); case = bad['cases'][0]; data = json.loads(case['input']['utf8Text']); data['initialState'][field] = value
            self.reinput(case, data); mutations.append(bad)
        for field, value in [('type', 'Unknown'), ('hero', None), ('health', True), ('health', 1.0), ('health', -2147483649), ('paused', 1), ('unknown', 0)]:
            bad = copy.deepcopy(root); case = bad['cases'][0]; data = json.loads(case['input']['utf8Text']); data['events'][0][field] = value
            self.reinput(case, data); mutations.append(bad)
        for bad in mutations:
            with patch.object(Path, 'read_bytes', return_value=json.dumps(bad).encode()), patch.object(oracle, 'observe_lifecycle') as reducer:
                with self.assertRaises(oracle.FixtureFormatError): oracle.load_lifecycle_fixture(self.fixture)
                reducer.assert_not_called()
        for raw in (b'{"schemaVersion":1,"schemaVersion":1,"cases":[]}', b'{', b''):
            with patch.object(Path, 'read_bytes', return_value=raw):
                with self.assertRaises(oracle.FixtureFormatError): oracle.load_lifecycle_fixture(self.fixture)

    @staticmethod
    def reinput(case, data):
        raw = json.dumps(data, separators=(',', ':')).encode(); case['input'] = {'utf8Text': raw.decode()}; case['inputSha256'] = hashlib.sha256(raw).hexdigest()

    def test_scope_is_infrastructure_not_diagnosis(self):
        case = oracle.load_lifecycle_fixture(self.fixture)[0]
        unknown = copy.deepcopy(case); unknown['oracleContract'] = 'future'
        large = copy.deepcopy(case); raw = b' ' * 65537; large['input'] = {'utf8Text': raw.decode()}; large['inputSha256'] = hashlib.sha256(raw).hexdigest()
        many = copy.deepcopy(case); data = json.loads(many['input']['utf8Text']); data['events'] *= 3; self.reinput(many, data)
        for bad in (unknown, large, many):
            with patch.object(oracle, 'observe_lifecycle') as reducer:
                with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_lifecycle_case(bad)
                reducer.assert_not_called()

    def test_deterministic_replay_and_input_immutability(self):
        cases = oracle.load_lifecycle_fixture(self.fixture); before = copy.deepcopy(cases)
        for case in cases:
            oracle.verify_lifecycle_case(case); oracle.verify_lifecycle_case(case)
            data = json.loads(bytes.fromhex(case['input']['utf8Hex']).decode() if 'utf8Hex' in case['input'] else case['input']['utf8Text'])
            original = copy.deepcopy(data)
            a = oracle.observe_lifecycle(data['initialState'], data['events'][0]); b = oracle.observe_lifecycle(data['initialState'], data['events'][0])
            self.assertEqual(a, b); self.assertEqual(original, data)
        self.assertEqual(before, cases)


class HollowKnightSkinTransactionGoldensTest(unittest.TestCase):
    fixture = ROOT / 'tools/skin-goldens/v1/transactions.json'

    def forward_cases(self):
        return [c for c in oracle.load_transaction_fixture(self.fixture)[:262] if c['oracleContract'] == oracle.TRANSACTION_CONTRACT]

    def test_corpus_full_forward_logs(self):
        cases = self.forward_cases()
        self.assertEqual(82, len(cases))
        self.assertEqual(164, sum(len(c['expected']) for c in cases))
        for case in cases:
            with self.subTest(case=case['caseId']): oracle.verify_transaction_case(case)


    @staticmethod
    def reinput(case, data):
        raw = json.dumps(data, ensure_ascii=False, separators=(',', ':')).encode()
        case['input'] = {'utf8Text': raw.decode()}; case['inputSha256'] = hashlib.sha256(raw).hexdigest()

    def test_recursive_state_commands_validator_leaves_and_order(self):
        self.assert_transaction_leaves(self.forward_cases())

    def assert_transaction_leaves(self, cases):
        case = cases[0]; pool = {}
        def collect(v):
            if isinstance(v, dict):
                for k, child in v.items():
                    if child is not None: pool[k] = copy.deepcopy(child)
                    collect(child)
            elif isinstance(v, list):
                for child in v: collect(child)
        for c in cases: collect(c['expected'])
        pool.update(originalFailure='FAILURE', rollbackFailure='ROLLBACK', failureReceipt=pool['completionReceipt'])
        def leaves(v, path=()):
            if isinstance(v, dict):
                for k, child in v.items(): yield from leaves(child, path + (k,))
            elif isinstance(v, list):
                for i, child in enumerate(v): yield from leaves(child, path + (i,))
            else: yield path, v
        for path, value in leaves(case['expected']):
            key = path[-1]
            if key == 'step': continue
            bad = copy.deepcopy(case); part = bad['expected']
            for k in path[:-1]: part = part[k]
            if key == 'type':
                replacement = {'type': 'Vanilla'} if value == 'Pack' else dict(type='Pack', id='changed', treeSha256='x', contentSha256='y', importReceiptSha256='z') if value == 'Vanilla' else dict(type='Rollback' if value == 'Apply' else 'Apply', correlation=copy.deepcopy(pool['correlation']))
                parent = bad['expected']
                for k in path[:-2]: parent = parent[k]
                parent[path[-2]] = replacement
            else:
                choices = {'phase': ('IDLE', 'ARMED'), 'state': ('CLEAR', 'ARMED'), 'mode': ('OFF', 'ON'), 'operation': ('MODE_ON', 'MODE_OFF'), 'skinStamp': ('7', '8')}
                part[key] = copy.deepcopy(pool[key]) if value is None else next(v for v in choices[key] if v != value) if key in choices else not value if type(value) is bool else value + 'changed'
            with self.subTest(path=path):
                with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)
        for i, row in enumerate(case['expected']):
            bad = copy.deepcopy(case); del bad['expected'][i]
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
            if row['commands']:
                bad = copy.deepcopy(case); bad['expected'][i]['commands'] = []
                with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)
                bad = copy.deepcopy(case); bad['expected'][i]['commands'] *= 2
                with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)
        bad = copy.deepcopy(case); bad['expected'][0], bad['expected'][1] = bad['expected'][1], bad['expected'][0]
        for i, row in enumerate(bad['expected']): row['step'] = i
        with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)

    def test_all_noop_all_invalid_and_replay_immutability(self):
        cases = self.forward_cases(); before = copy.deepcopy(cases)
        for diagnosis in ('stale-phase', 'invalid-state'):
            with patch.object(oracle, 'decide_transaction', side_effect=lambda s,e: dict(state=copy.deepcopy(s), diagnosis=diagnosis, commands=[])):
                with self.assertRaises(AssertionError): oracle.verify_transaction_case(cases[0])
        for c in cases:
            oracle.verify_transaction_case(c); oracle.verify_transaction_case(c)
            data = oracle._tx_case(c); original = copy.deepcopy(data)
            self.assertEqual(oracle.decide_transaction(data['initialState'], data['events'][0]), oracle.decide_transaction(data['initialState'], data['events'][0]))
            self.assertEqual(original, data)
        self.assertEqual(before, cases)

    def test_recursive_closed_types_and_malformed_before_scope(self):
        case = self.forward_cases()[0]; data = oracle._tx_case(case)
        def objects(v, path=()):
            if type(v) is dict:
                yield path, v
                for k, child in v.items(): yield from objects(child, path+(k,))
            elif type(v) is list:
                for i, child in enumerate(v): yield from objects(child, path+(i,))
        for path, obj in objects(data):
            for damage in ('missing', 'extra'):
                bad = copy.deepcopy(case); d = copy.deepcopy(data); part = d
                for k in path: part = part[k]
                if damage == 'missing': del part[next(iter(obj))]
                else: part['unexpected'] = None
                self.reinput(bad,d)
                with patch.object(oracle, 'decide_transaction') as reduce:
                    with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
                    reduce.assert_not_called()
        for value in ['01','+1','-0','9223372036854775808','-9223372036854775809',1,True,None]:
            bad = copy.deepcopy(case); d = copy.deepcopy(data); d['initialState']['phase'] = 'BLOCKED'; d['initialState']['activation']['skinStamp'] = value; self.reinput(bad,d)
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
        root = json.loads(self.fixture.read_bytes())
        for bad in [dict(root,cases=[]),dict(root,schemaVersion=True),dict(root,unknown=0),dict(root,cases=root['cases']+[root['cases'][0]]),{}]:
            with patch.object(Path,'read_bytes',return_value=json.dumps(bad).encode()):
                with self.assertRaises(oracle.FixtureFormatError): self.forward_cases()
        for raw in [b'',b'{',b'\xff',b'{"schemaVersion":1,"schemaVersion":1,"cases":[]}',b'{"x":"\\ud800"}']:
            with patch.object(Path,'read_bytes',return_value=raw):
                with self.assertRaises(oracle.FixtureFormatError): self.forward_cases()
        for raw in [b'\xff',b'{"initialState":0,"initial\\u0053tate":0,"events":[]}',b'{"x":"\\ud800"}']:
            bad=copy.deepcopy(case); bad['input']={'utf8Hex':raw.hex()}; bad['inputSha256']=hashlib.sha256(raw).hexdigest()
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
        bad=copy.deepcopy(case); bad['inputSha256']='0'*64
        with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
        with patch.object(Path,'read_bytes',side_effect=FileNotFoundError('missing')):
            with self.assertRaises(oracle.FixtureFormatError): self.forward_cases()
        # Exercise the real loader/read/decode boundary; only known absence is fixture damage.
        for error in (PermissionError('permission denied'), OSError('unexpected read failure')):
            with self.subTest(io=type(error).__name__), patch.object(Path,'read_bytes',side_effect=error):
                with self.assertRaises(Exception) as caught: self.forward_cases()
                self.assertIs(error, caught.exception)
        with patch.object(Path,'read_bytes',return_value=b'\xff'):
            with self.assertRaises(oracle.FixtureFormatError): self.forward_cases()

    def test_supported_classifier_before_semantics_after_complete_types(self):
        case = self.forward_cases()[0]; data = oracle._tx_case(case); corr = data['events'][1]['correlation']
        excluded = ['ApplyFailed','RollbackVerified','RollbackFailed','CompletionRejected','CompletionIndeterminate']
        for mode in excluded + ['ROLLBACK_PENDING','ROLLED_BACK','BLOCKED','contract','bytes','events']:
            bad=copy.deepcopy(case); d=copy.deepcopy(data)
            if mode in excluded:
                event=dict(type=mode,correlation=copy.deepcopy(corr))
                if mode in ('ApplyFailed','RollbackFailed','CompletionRejected'): event['code']='bad-code'
                if mode=='RollbackVerified': event['freshBinding']=False
                if mode=='RollbackFailed': event.update(persistedFailureReceipt=None,verifiedHead=None)
                d['events'][0]=event
            elif mode=='contract': bad['oracleContract']='future'
            elif mode=='events':
                while len(d['events'])<33:
                    i=len(d['events']); d['events'].append(copy.deepcopy(d['events'][0])); bad['expected'].append(dict(copy.deepcopy(bad['expected'][0]),step=i))
            elif mode!='bytes': d['initialState'].update(phase=mode,binding=None)
            self.reinput(bad,d)
            if mode=='bytes':
                raw=(bad['input']['utf8Text']+' '*65537).encode(); bad['input']={'utf8Text':raw.decode()}; bad['inputSha256']=hashlib.sha256(raw).hexdigest()
            with patch.object(oracle,'decide_transaction') as reduce, patch.object(oracle,'transaction_state_valid') as validator:
                with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)
                reduce.assert_not_called(); validator.assert_not_called()
            if mode in excluded:
                d['events'][0]['correlation']['binding']['value']=4; self.reinput(bad,d)
                with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)

    def test_named_semantic_isolation_and_repaired_controls(self):
        cases = {c['caseId']:c for c in self.forward_cases()}
        # Fixed trusted in-memory Python probes only; no production source mutation.
        case=cases['receipt-only-is-not-visual-change']; d=oracle._tx_case(case); e=d['events'][0]['envelope']
        p,t=e['prior'],e['target']
        self.assertEqual(('OFF','ON'),(p['mode'],t['mode']))
        self.assertEqual(['importReceiptSha256'],[k for k in p['active'] if p['active'][k]!=t['active'][k]])
        self.assertEqual('invalid-envelope',oracle.decide_transaction(d['initialState'],d['events'][0])['diagnosis'])
        with patch.object(oracle,'_tx_same_visual',side_effect=lambda a,b:a==b):
            self.assertEqual('prepare',oracle.decide_transaction(d['initialState'],d['events'][0])['diagnosis'])
            with self.assertRaises(AssertionError): oracle.verify_transaction_case(case)
        repair=copy.deepcopy(d['events'][0]); repair['envelope']['priorEstablishedOnBinding']=False
        self.assertEqual('prepare',oracle.decide_transaction(d['initialState'],repair)['diagnosis'])
        for field in ('newGenerationId','newGenerationSha256'):
            case=cases['completion-antireuse-'+field]; d=oracle._tx_case(case); s=d['initialState']; event=d['events'][0]; r=event['commitReceipt']; arm=s['armCommitReceipt']
            self.assertTrue(oracle._tx_receipt(r,arm['newGenerationId'],arm['newGenerationSha256']))
            self.assertTrue(oracle._tx_head(event['verifiedHead'],r,s['pendingClosure'],oracle._tx_clear()))
            self.assertEqual('committed',oracle.decide_transaction(s,d['events'][1])['diagnosis'])
            other='newGenerationSha256' if field=='newGenerationId' else 'newGenerationId'
            base='baseGenerationSha256' if other.endswith('Sha256') else 'baseGenerationId'
            def omit_one(state,receipt):
                arm=state['armCommitReceipt']
                return arm is not None and oracle._tx_receipt(receipt,arm['newGenerationId'],arm['newGenerationSha256']) and receipt[other]!=state['envelope'][base]
            with patch.object(oracle,'_tx_follows',side_effect=omit_one):
                self.assertEqual('committed',oracle.decide_transaction(s,event)['diagnosis'])
                with self.assertRaises(AssertionError): oracle.verify_transaction_case(case)
        case = cases['invalid-state-original-code-lower']; d = oracle._tx_case(case)
        match = oracle._tx_match
        with patch.object(oracle, '_tx_match', side_effect=lambda pattern,text: True if pattern is oracle.FAILURE_CODE else match(pattern,text)):
            self.assertEqual('terminal', oracle.decide_transaction(d['initialState'], d['events'][0])['diagnosis'])
            with self.assertRaises(AssertionError): oracle.verify_transaction_case(case)
        repaired = dict(d['initialState'], originalFailure='FAILED')
        self.assertEqual('terminal', oracle.decide_transaction(repaired,d['events'][0])['diagnosis'])
        for name,case in cases.items():
            if name.startswith(('receipt-2-', 'receipt-4-')):
                d=oracle._tx_case(case); event=d['events'][0]
                self.assertEqual(event['commitReceipt']['newGenerationId'],event['verifiedHead']['generationId'])
                self.assertEqual(event['commitReceipt']['newGenerationSha256'],event['verifiedHead']['generationSha256'])
                self.assertIn(oracle.decide_transaction(d['initialState'],d['events'][1])['diagnosis'],('apply','committed'))


class HollowKnightSkinRollbackGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    forward_cases = HollowKnightSkinTransactionGoldensTest.forward_cases
    reinput = staticmethod(HollowKnightSkinTransactionGoldensTest.reinput)
    assert_transaction_leaves = HollowKnightSkinTransactionGoldensTest.assert_transaction_leaves
    # Reuse assertion helpers without duplicating the accepted forward test suite.
    def rollback_cases(self):
        return [c for c in oracle.load_transaction_fixture(self.fixture)[:262] if c['oracleContract'] == oracle.TRANSACTION_ROLLBACK_CONTRACT]

    def test_rollback_inventory_and_forward_byte_semantic_pins(self):
        raw = self.fixture.read_bytes()
        # Accepted original file had CRLF. Only its closing array/root suffix is replaced.
        suffix = b'\r\n  ]\r\n}\r\n'
        original = raw[:997379 - len(suffix)] + suffix
        self.assertEqual('981ea898ba782abf19f8b77d2132341c63b42180577d7b04f1755f783cb8b761', hashlib.sha256(original).hexdigest())
        cases = oracle.load_transaction_fixture(self.fixture)
        forward = cases[:82]
        self.assertTrue(all(c['oracleContract'] == oracle.TRANSACTION_CONTRACT for c in forward))
        self.assertEqual(82, len(forward)); self.assertEqual(164, sum(len(c['expected']) for c in forward))
        semantic = json.dumps(forward, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('utf-8')
        self.assertEqual('32e70c5a41e5eff6f22a0a8ba722a7473f24db42ee548dfbea779c1b735c672b', hashlib.sha256(semantic).hexdigest())
        rollback = self.rollback_cases()
        self.assertEqual(33, len(rollback)); self.assertEqual(77, sum(len(c['expected']) for c in rollback))
        self.assertEqual(115, len([c for c in cases[:262] if c['oracleContract'] in (oracle.TRANSACTION_CONTRACT, oracle.TRANSACTION_ROLLBACK_CONTRACT)]))

    def test_rollback_full_logs_replay_and_substitutions(self):
        cases = self.rollback_cases(); before = copy.deepcopy(cases)
        for case in cases:
            with self.subTest(case=case['caseId']):
                oracle.verify_transaction_case(case); oracle.verify_transaction_case(case)
                data = oracle._tx_case(case); original = copy.deepcopy(data)
                options = {'contract': oracle.TRANSACTION_ROLLBACK_CONTRACT}
                self.assertEqual(oracle.decide_transaction(data['initialState'], data['events'][0], **options), oracle.decide_transaction(data['initialState'], data['events'][0], **options))
                self.assertEqual(original, data)
        self.assertEqual(before, cases)
        for diagnosis in ('stale-phase', 'invalid-state'):
            with patch.object(oracle, 'decide_transaction', side_effect=lambda s,e,**kw: dict(state=copy.deepcopy(s), diagnosis=diagnosis, commands=[])):
                for case in (self.forward_cases()[0], cases[1]):
                    with self.assertRaises(AssertionError): oracle.verify_transaction_case(case)

    def test_rollback_recursive_leaves_commands_and_order(self):
        case = next(c for c in self.rollback_cases() if c['caseId'] == 'rollback-history-unestablished-pack')
        self.assert_transaction_leaves([case])

    def test_rollback_scope_and_default_api_remain_separate(self):
        cases = self.rollback_cases(); case = cases[1]; data = oracle._tx_case(case)
        for c in cases:
            if c['caseId'].startswith('rollback-repair-'):
                d = oracle._tx_case(c)
                with self.assertRaises(oracle.OracleOutOfScope): oracle.transaction_state_valid(d['initialState'])
                self.assertTrue(oracle.transaction_state_valid(d['initialState'], contract=oracle.TRANSACTION_ROLLBACK_CONTRACT))
        for event in (data['events'][3], data['events'][4]):
            with self.assertRaises(oracle.OracleOutOfScope): oracle.decide_transaction(data['initialState'], event)
        for mode in ('RollbackFailed', 'CompletionRejected', 'CompletionIndeterminate', 'BLOCKED', 'contract', 'bytes', 'events'):
            bad=copy.deepcopy(case); d=copy.deepcopy(data)
            if mode == 'BLOCKED': d['initialState'].update(phase=mode,binding=None)
            elif mode == 'contract': bad['oracleContract']='future'
            elif mode == 'events':
                while len(d['events'])<33:
                    i=len(d['events']); d['events'].append(copy.deepcopy(d['events'][0])); bad['expected'].append(dict(copy.deepcopy(bad['expected'][0]),step=i))
            elif mode != 'bytes':
                event=dict(type=mode,correlation=copy.deepcopy(d['events'][3]['correlation']))
                if mode != 'CompletionIndeterminate': event['code']='invalid-code'
                if mode == 'RollbackFailed': event.update(persistedFailureReceipt=None,verifiedHead=None)
                d['events'][0]=event
            self.reinput(bad,d)
            if mode == 'bytes':
                raw=(bad['input']['utf8Text']+' '*65537).encode(); bad['input']={'utf8Text':raw.decode()};bad['inputSha256']=hashlib.sha256(raw).hexdigest()
            with patch.object(oracle,'decide_transaction') as reducer, patch.object(oracle,'transaction_state_valid') as validator:
                with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)
                reducer.assert_not_called(); validator.assert_not_called()
            d['initialState']['activation']['skinStamp']='01';self.reinput(bad,d)
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)

    def test_rollback_guards_are_isolated_and_repairs_positive(self):
        cases = {c['caseId']:c for c in self.rollback_cases()}; options={'contract':oracle.TRANSACTION_ROLLBACK_CONTRACT}
        for name in ('rollback-proof-established-pack','rollback-proof-unestablished-pack'):
            data=oracle._tx_case(cases[name]);state=data['initialState'];bad,good=data['events']
            self.assertEqual(bad['correlation'],good['correlation']); self.assertEqual(state['binding'],good['correlation']['binding'])
            self.assertEqual(state['envelope']['priorEstablishedOnBinding'],bad['freshBinding'])
            self.assertNotEqual(state['envelope']['priorEstablishedOnBinding'],good['freshBinding'])
            self.assertEqual('invalid-binding-proof',oracle.decide_transaction(state,bad,**options)['diagnosis'])
            self.assertEqual('commit-closure',oracle.decide_transaction(state,good,**options)['diagnosis'])
        data=oracle._tx_case(cases['rollback-completion-head-target']);s=data['initialState'];bad,good=data['events']
        self.assertEqual(bad['commitReceipt'],good['commitReceipt']);self.assertTrue(oracle._tx_follows(s,bad['commitReceipt']))
        self.assertEqual(['activation'],[k for k in bad['verifiedHead'] if bad['verifiedHead'][k]!=good['verifiedHead'][k]])
        def omit_activation(head,receipt,activation,lock):
            return head is not None and receipt is not None and head['generationId']==receipt['newGenerationId'] and head['generationSha256']==receipt['newGenerationSha256'] and head['interlock']==lock
        with patch.object(oracle,'_tx_head',side_effect=omit_activation):
            self.assertEqual('committed',oracle.decide_transaction(s,bad,**options)['diagnosis'])
            with self.assertRaises(AssertionError):oracle.verify_transaction_case(cases['rollback-completion-head-target'])
        for field in ('expectedGenerationId','expectedGenerationSha256'):
            name='rollback-completion-expected-base-'+field
            d=oracle._tx_case(cases[name]);state=d['initialState'];bad,good=d['events']
            self.assertEqual([field],[k for k in bad['commitReceipt'] if bad['commitReceipt'][k]!=good['commitReceipt'][k]])
            self.assertEqual(bad['verifiedHead'],good['verifiedHead'])
            self.assertTrue(oracle._tx_head(bad['verifiedHead'],bad['commitReceipt'],state['pendingClosure'],oracle._tx_clear()))
            def omit_expected(receipt,generation,digest):
                return (receipt is not None and all(oracle._tx_match(oracle.UUID if k.endswith('Id') else oracle.HASH,v) for k,v in receipt.items()) and
                    (field=='expectedGenerationId' or receipt['expectedGenerationId']==generation) and
                    (field=='expectedGenerationSha256' or receipt['expectedGenerationSha256']==digest) and
                    receipt['newGenerationId']!=generation and receipt['newGenerationSha256']!=digest)
            with patch.object(oracle,'_tx_receipt',side_effect=omit_expected):
                self.assertEqual('committed',oracle.decide_transaction(state,bad,**options)['diagnosis'])
                with self.assertRaises(AssertionError):oracle.verify_transaction_case(cases[name])
            self.assertEqual('committed',oracle.decide_transaction(state,good,**options)['diagnosis'])
        for name in ('pending-original','pending-closure','rolled-original','rolled-closure'):
            invalid=oracle._tx_case(cases['rollback-invalid-'+name]);repair=oracle._tx_case(cases['rollback-repair-'+name])
            self.assertEqual(1,sum(invalid['initialState'][k]!=repair['initialState'][k] for k in invalid['initialState']))
            self.assertFalse(oracle.transaction_state_valid(invalid['initialState'],**options));self.assertTrue(oracle.transaction_state_valid(repair['initialState'],**options))

    def test_rollback_trusted_python_guard_omissions_kill(self):
        path=ROOT/'tools/skins/skin_golden_oracle.py';raw=path.read_bytes();source=raw.decode('utf-8')
        cases={c['caseId']:c for c in self.rollback_cases()}
        probes=[
            ('rollback-proof-established-pack', "if event['freshBinding'] == e['priorEstablishedOnBinding']:", 'if False:'),
            ('rollback-proof-unestablished-pack', "if event['freshBinding'] == e['priorEstablishedOnBinding']:", 'if False:'),
            ('rollback-invalid-pending-original', "return s['originalFailure'] is not None and s['pendingClosure'] ==", "return s['pendingClosure'] =="),
            ('rollback-invalid-rolled-original', "return s['originalFailure'] is not None and s['pendingClosure'] ==", "return s['pendingClosure'] =="),
            ('rollback-invalid-pending-closure', "return s['originalFailure'] is not None and s['pendingClosure'] == (_tx_prior_closure(e) if s['phase'] == 'ROLLED_BACK' else None)", "return s['originalFailure'] is not None"),
            ('rollback-invalid-rolled-closure', "return s['originalFailure'] is not None and s['pendingClosure'] == (_tx_prior_closure(e) if s['phase'] == 'ROLLED_BACK' else None)", "return s['originalFailure'] is not None"),
        ]
        for name,guard,omitted in probes:
            with self.subTest(case=name):
                self.assertEqual(1,source.count(guard))
                namespace={'__file__':str(path),'__name__':'rollback_guard_probe'}
                exec(compile(source.replace(guard,omitted),'<in-memory-rollback-guard-probe>','exec'),namespace)
                with self.assertRaises(AssertionError):namespace['verify_transaction_case'](cases[name])
        self.assertEqual(raw,path.read_bytes())


class HollowKnightSkinFailureGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    reinput = staticmethod(HollowKnightSkinTransactionGoldensTest.reinput)
    assert_transaction_leaves = HollowKnightSkinTransactionGoldensTest.assert_transaction_leaves

    def failure_cases(self):
        # Original IDs are pinned by the accepted138 byte/semantic checks below.
        return [c for c in oracle.load_transaction_fixture(self.fixture)[:138] if c['oracleContract'] == oracle.TRANSACTION_FAILURE_CONTRACT]

    def test_failure_inventory_and_accepted115_byte_semantic_pins(self):
        raw = self.fixture.read_bytes(); suffix = b'\r\n  ]\r\n}\r\n'
        self.assertEqual('5066d2dd810e3c8538ecc4f9ae26927b24138fe2847b79f9375774fa37b5e7ac', hashlib.sha256(raw[:1528504] + suffix).hexdigest())
        cases = oracle.load_transaction_fixture(self.fixture); accepted = cases[:115]
        self.assertEqual(115, len(accepted)); self.assertEqual(241, sum(len(c['expected']) for c in accepted))
        self.assertEqual('1a0f282f324641738ee7b0c0b76f4430900ba7efd80139521b67cedd5423e48d', hashlib.sha256(json.dumps(accepted, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest())
        failure = self.failure_cases()
        self.assertEqual(23, len(failure)); self.assertEqual(136, sum(len(c['expected']) for c in failure)); self.assertEqual(138, len([c for c in cases[:262] if not c['caseId'].startswith('failure-proof-')]))
        for family, count in [('history', 10), ('code', 3), ('correlation', 5), ('terminal', 5)]:
            self.assertEqual(count, sum(c['caseId'].startswith('failure-' + family + '-') for c in failure))

    def test_failure_full_logs_replay_immutability_and_substitutions(self):
        cases = self.failure_cases(); before = copy.deepcopy(cases)
        for case in cases:
            oracle.verify_transaction_case(case); oracle.verify_transaction_case(case)
            data = oracle._tx_case(case); original = copy.deepcopy(data)
            options = {'contract': oracle.TRANSACTION_FAILURE_CONTRACT}
            self.assertEqual(oracle.decide_transaction(data['initialState'], data['events'][0], **options), oracle.decide_transaction(data['initialState'], data['events'][0], **options))
            self.assertEqual(original, data)
        self.assertEqual(before, cases)
        for diagnosis in ('stale-phase', 'invalid-state'):
            with patch.object(oracle, 'decide_transaction', side_effect=lambda s,e,**kw: dict(state=copy.deepcopy(s), diagnosis=diagnosis, commands=[])):
                for case in cases:
                    with self.subTest(case=case['caseId'], diagnosis=diagnosis):
                        with self.assertRaises(AssertionError): oracle.verify_transaction_case(case)

    def test_failure_recursive_leaves_commands_and_order(self):
        cases = oracle.load_transaction_fixture(self.fixture)
        for name in ('failure-history-reject-rollback-persisted', 'failure-history-rolled-rejected', 'failure-history-applied-indeterminate'):
            case = next(c for c in cases if c['caseId'] == name)
            # Only the first case is mutated; all cases supply representable nullable replacements.
            self.assert_transaction_leaves([case] + cases)

    def test_failure_contract_scope_preserves_older_contracts_and_type_precedence(self):
        cases = self.failure_cases()
        self.assertEqual(set(oracle._TX_PHASES), set(oracle._tx_scope(oracle.TRANSACTION_FAILURE_CONTRACT)[0]))
        self.assertEqual(set(oracle._TX_UNIONS['Event']), set(oracle._tx_scope(oracle.TRANSACTION_FAILURE_CONTRACT)[1]))
        for case in cases:
            for contract in (oracle.TRANSACTION_CONTRACT, oracle.TRANSACTION_ROLLBACK_CONTRACT):
                bad = copy.deepcopy(case); bad['oracleContract'] = contract
                with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                    with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)
                    reducer.assert_not_called(); validator.assert_not_called()
        baseline = cases[0]
        for mode in ('contract', 'bytes', 'events'):
            bad = copy.deepcopy(baseline); data = oracle._tx_case(bad)
            if mode == 'contract': bad['oracleContract'] = 'future'
            if mode == 'events':
                while len(data['events']) < 33:
                    i = len(data['events']); data['events'].append(copy.deepcopy(data['events'][0])); bad['expected'].append(dict(copy.deepcopy(bad['expected'][0]), step=i))
            self.reinput(bad, data)
            if mode == 'bytes':
                raw = (bad['input']['utf8Text'] + ' ' * 65537).encode(); bad['input'] = {'utf8Text': raw.decode()}; bad['inputSha256'] = hashlib.sha256(raw).hexdigest()
            with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)
                reducer.assert_not_called(); validator.assert_not_called()
            bad['expected'][0]['state']['activation']['skinStamp'] = '01'
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
        for field in ('freshBinding', 'priorEstablishedOnBinding'):
            bad = copy.deepcopy(baseline); data = oracle._tx_case(bad)
            if field == 'freshBinding': data['events'][5][field] = 1
            else: data['events'][0]['envelope'][field] = 1
            self.reinput(bad, data)
            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)

    def test_failure_code_and_correlation_panels_are_isolated_repairs(self):
        for case in self.failure_cases():
            data = oracle._tx_case(case); state = data['initialState']; events = data['events']
            if case['caseId'].startswith('failure-code-'):
                self.assertEqual(['', 'lower', 'A' * 129, 'A' * 128], [e['code'] for e in events])
                for event in events[:-1]: self.assertEqual(['code'], [k for k in event if event[k] != events[-1][k]])
                self.assertEqual(['invalid-code'] * 3, [r['diagnosis'] for r in case['expected'][:-1]])
                self.assertNotEqual('invalid-code', case['expected'][-1]['diagnosis'])
            elif case['caseId'].startswith('failure-correlation-'):
                self.assertEqual(5, len(events))
                self.assertEqual(['stale-correlation'] * 4, [r['diagnosis'] for r in case['expected'][:-1]])
                for i, event in enumerate(events[:-1]):
                    self.assertEqual(['correlation'], [k for k in event if event[k] != events[-1][k]])
                    self.assertEqual(['transactionId' if i < 2 else 'binding'], [k for k in event['correlation'] if event['correlation'][k] != events[-1]['correlation'][k]])
                self.assertEqual(state['binding'], events[-1]['correlation']['binding'])
            else: continue
            self.assertTrue(all(r['inputStateValid'] and r['stateValid'] for r in case['expected']))
            for row in case['expected'][:-1]: self.assertEqual(state, row['state']); self.assertEqual([], row['commands'])

    def test_failure_named_behavior_omissions_kill(self):
        path = ROOT / 'tools/skins/skin_golden_oracle.py'; raw = path.read_bytes(); source = raw.decode()
        cases = {c['caseId']: c for c in self.failure_cases()}
        probes = [
            ('terminal-before-begin', 'failure-terminal-persisted', "if phase in ('COMMITTED', 'BLOCKED'): return result('terminal')", "if phase == 'COMMITTED': return result('terminal')"),
            ('rejection-clears-target', 'failure-history-reject-restored-established-pack', "        if phase == 'APPLIED':\n            return result('rollback', [dict(type='Rollback', correlation=c)], phase='ROLLBACK_PENDING', pendingClosure=None, originalFailure=event['code'])", "        if phase == 'APPLIED':\n            return result('rollback', [dict(type='Rollback', correlation=c)], phase='ROLLBACK_PENDING', originalFailure=event['code'])"),
            ('rejected-retains-prior', 'failure-history-rolled-rejected', "return result('rollback-closure-rejected', phase='BLOCKED', rollbackFailure=event['code'])", "return result('rollback-closure-rejected', phase='BLOCKED', pendingClosure=None, rollbackFailure=event['code'])"),
            ('indeterminate-blocks-applied', 'failure-history-applied-indeterminate', "return result('completion-indeterminate', phase='BLOCKED')", "return result('completion-indeterminate')"),
            ('indeterminate-blocks-rolled', 'failure-history-rolled-indeterminate', "return result('completion-indeterminate', phase='BLOCKED')", "return result('completion-indeterminate')"),
            ('persist-exact-proof', 'failure-history-reject-rollback-persisted', "persisted = _tx_follows(s, receipt) and _tx_head(event['verifiedHead'], receipt, e['prior'], failed_lock)", 'persisted = False'),
            ('missing-proof-retains-arm', 'failure-history-reject-rollback-unpersisted', "persisted = _tx_follows(s, receipt) and _tx_head(event['verifiedHead'], receipt, e['prior'], failed_lock)", 'persisted = True'),
            ('rollbackfailed-code', 'failure-code-rollbackfailed', "if (phase, kind) == ('ROLLBACK_PENDING', 'RollbackFailed'):\n        if not _tx_match(FAILURE_CODE, event['code']):", "if (phase, kind) == ('ROLLBACK_PENDING', 'RollbackFailed'):\n        if False:"),
            ('rejection-code-applied', 'failure-code-applied-rejected', "if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionRejected':\n        if not _tx_match(FAILURE_CODE, event['code']):", "if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionRejected':\n        if False:"),
            ('rejection-code-rolled', 'failure-code-rolled-rejected', "if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionRejected':\n        if not _tx_match(FAILURE_CODE, event['code']):", "if phase in ('APPLIED', 'ROLLED_BACK') and kind == 'CompletionRejected':\n        if False:"),
        ]
        # Fixed trusted Python source substitutions only. No shell or external input.
        for name, case_id, guard, omitted in probes:
            with self.subTest(omission=name, case=case_id):
                self.assertEqual(1, source.count(guard))
                namespace = {'__file__': str(path), '__name__': 'failure_behavior_probe'}
                exec(compile(source.replace(guard, omitted), '<in-memory-failure-behavior-probe>', 'exec'), namespace)
                with self.assertRaises(AssertionError): namespace['verify_transaction_case'](cases[case_id])
        self.assertEqual(raw, path.read_bytes())


class HollowKnightSkinFailureProofGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    reinput = staticmethod(HollowKnightSkinTransactionGoldensTest.reinput)
    options = {'contract': oracle.TRANSACTION_FAILURE_CONTRACT}

    def proof_cases(self):
        return [c for c in oracle.load_transaction_fixture(self.fixture) if c['caseId'].startswith('failure-proof-')]

    @staticmethod
    def semantic(value):
        return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest()

    def test_proof_inventory_accepted138_exact_bytes_records_and_logs(self):
        raw = self.fixture.read_bytes()
        self.assertEqual('ce51b6aed12113dbdf457edaa8c0518b4ae668883978c541392d886dcbe4b482', hashlib.sha256(raw[:2373974]).hexdigest())
        all_cases = oracle.load_transaction_fixture(self.fixture); old = all_cases[:138]
        self.assertEqual('f3d7dafecfb076c1c7694db97a32d51b196b445ff0285264b2f924b7df136d13', self.semantic(old))
        self.assertEqual('1d744e8af105e0cf5e4b487aea793d41657e802ef6c84129db78b66318396445', self.semantic([c['expected'] for c in old]))
        self.assertEqual('fcbe5e1692457e2c827c7411db25e161a5acf94252552ef94b3b94db64cbeb96', self.semantic(old[115:]))
        self.assertEqual('6ee13a79be90b5b61eff15b5daf32c52ccf084960b2a6899c0537803e8f3c418', self.semantic([c['expected'] for c in old[115:]]))
        self.assertEqual((262, 625), (len(all_cases[:262]), sum(len(c['expected']) for c in all_cases[:262])))
        cases = self.proof_cases(); self.assertEqual((124, 248), (len(cases), sum(len(c['expected']) for c in cases)))
        self.assertEqual(96, sum(not c['caseId'].startswith('failure-proof-blocked-') for c in cases))
        self.assertEqual(28, sum(c['caseId'].startswith('failure-proof-blocked-') for c in cases))
        self.assertTrue(all(c['oracleContract'] == oracle.TRANSACTION_FAILURE_CONTRACT for c in cases))

    def test_proof_full_logs_and_separate_original_pending_repairs(self):
        cases = {c['caseId']: c for c in self.proof_cases()}
        for name, case in cases.items():
            with self.subTest(case=name):
                before = copy.deepcopy(case); oracle.verify_transaction_case(case); oracle.verify_transaction_case(case)
                self.assertEqual(before, case)
                if not name.endswith('-negative'): continue
                repaired = cases[name[:-9] + '-repair']
                bad, good = oracle._tx_case(case), oracle._tx_case(repaired)
                if '-blocked-' not in name:
                    self.assertEqual(bad['initialState'], good['initialState'])
                    self.assertEqual('ROLLBACK_PENDING', bad['initialState']['phase'])
                    self.assertEqual(bad['events'][1], good['events'][0])
                    self.assertEqual('ARMED', case['expected'][0]['state']['interlock']['state'])
                    self.assertIsNone(case['expected'][0]['state']['failureReceipt'])
                    self.assertEqual('ROLLBACK_FAILED', repaired['expected'][0]['state']['interlock']['state'])
                    self.assertEqual(good['events'][0]['persistedFailureReceipt'], repaired['expected'][0]['state']['failureReceipt'])
                    self.assertEqual(['rollback-failed', 'terminal'], [r['diagnosis'] for r in case['expected']])
                    self.assertEqual(case['expected'][0]['state'], case['expected'][1]['state'])
                else:
                    self.assertEqual(bad['events'], good['events'])
                    self.assertFalse(oracle.transaction_state_valid(bad['initialState'], **self.options))
                    self.assertTrue(oracle.transaction_state_valid(good['initialState'], **self.options))
                    self.assertEqual(['invalid-state'] * 2, [r['diagnosis'] for r in case['expected']])
                    self.assertEqual(['terminal'] * 2, [r['diagnosis'] for r in repaired['expected']])

    def test_proof_all_repaired_positives_kill_default_substitutions(self):
        for case in self.proof_cases():
            if not case['caseId'].endswith('-repair'): continue
            for diagnosis in ('stale-phase', 'invalid-state'):
                with self.subTest(case=case['caseId'], substitution=diagnosis):
                    with patch.object(oracle, 'decide_transaction', side_effect=lambda s,e,**kw: dict(state=copy.deepcopy(s), diagnosis=diagnosis, commands=[])):
                        with self.assertRaisesRegex(AssertionError, 'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)

    @staticmethod
    def receipt_omitting(r, generation, digest, omission):
        if r is None: return False
        checks = [('syntax-' + k, oracle._tx_match(oracle.UUID if k.endswith('Id') else oracle.HASH, v)) for k,v in r.items()]
        checks += [('expected-mismatch-expectedGenerationId', r['expectedGenerationId'] == generation), ('expected-mismatch-expectedGenerationSha256', r['expectedGenerationSha256'] == digest), ('new-equals-arm-newGenerationId', r['newGenerationId'] != generation), ('new-equals-arm-newGenerationSha256', r['newGenerationSha256'] != digest)]
        return all(ok for name,ok in checks if name != omission)

    @staticmethod
    def follows_omitting(s, r, omission):
        arm, e = s['armCommitReceipt'], s['envelope']
        return (arm is not None and oracle._tx_receipt(r, arm['newGenerationId'], arm['newGenerationSha256']) and
                (omission == 'new-equals-base-newGenerationId' or r['newGenerationId'] != e['baseGenerationId']) and
                (omission == 'new-equals-base-newGenerationSha256' or r['newGenerationSha256'] != e['baseGenerationSha256']))

    @staticmethod
    def equal_except(a, b, path):
        # Omit only the named equality component; retain every sibling comparison.
        if not path: return True
        if type(a) is not dict or type(b) is not dict or a.keys() != b.keys(): return False
        return all(HollowKnightSkinFailureProofGoldensTest.equal_except(a[k], b[k], path[1:]) if k == path[0] else a[k] == b[k] for k in a)

    def test_proof_named_receipt_and_head_guard_omissions_kill(self):
        cases = {c['caseId']: c for c in self.proof_cases()}
        snapshot_paths = [('mode', ['mode']), ('selectedPackId', ['selectedPackId']), ('skinStamp', ['skinStamp']), ('active-id', ['active','id']), ('active-treeSha256', ['active','treeSha256']), ('active-contentSha256', ['active','contentSha256']), ('active-importReceiptSha256', ['active','importReceiptSha256']), ('visual-variant', ['active'])]
        head_paths = [('head-generationId', ['generationId']), ('head-generationSha256', ['generationSha256'])]
        head_paths += [('head-activation-' + n, ['activation'] + p) for n,p in snapshot_paths]
        head_paths += [('lock-' + k, ['interlock', k]) for k in ('state','transactionId','operation','baseGenerationId','baseGenerationSha256')]
        head_paths += [('lock-' + side + '-' + n, ['interlock', side] + p) for side in ('prior','target') for n,p in snapshot_paths]
        head_paths += [('lock-binding', ['interlock','bindingToken','value']), ('lock-established', ['interlock','priorEstablishedOnBinding']), ('lock-originalFailure', ['interlock','originalFailure']), ('lock-rollbackFailure', ['interlock','rollbackFailure'])]
        probes = [(n, '_tx_head', lambda h,r,a,l,p=p: h is not None and r is not None and self.equal_except(h, dict(generationId=r['newGenerationId'], generationSha256=r['newGenerationSha256'], activation=a, interlock=l), p)) for n,p in head_paths]
        probes.append(('missing-head', '_tx_head', lambda h,r,a,l: r is not None and (h is None or oracle_head(h,r,a,l))))
        oracle_head = oracle._tx_head
        for family in ('syntax', 'expected-mismatch', 'new-equals-arm'):
            fields = ('newGenerationId','newGenerationSha256') if family != 'expected-mismatch' else ('expectedGenerationId','expectedGenerationSha256')
            for field in fields:
                name = family + '-' + field
                probes.append((name, '_tx_receipt', lambda r,g,d,n=name: self.receipt_omitting(r,g,d,n)))
        for field in ('newGenerationId','newGenerationSha256'):
            name = 'new-equals-base-' + field
            probes.append((name, '_tx_follows', lambda s,r,n=name: self.follows_omitting(s,r,n)))
        self.assertEqual(44, len(probes))
        for name, helper, replacement in probes:
            bad = cases['failure-proof-' + name + '-negative']; repaired = cases['failure-proof-' + name + '-repair']
            data = oracle._tx_case(bad); state = data['initialState']; event = data['events'][0]
            with self.subTest(omission=name):
                self.assertTrue(oracle.transaction_state_valid(state, **self.options))
                if helper == '_tx_head': self.assertTrue(oracle._tx_follows(state, event['persistedFailureReceipt']))
                else:
                    lock = dict(state['interlock'], state='ROLLBACK_FAILED', originalFailure=state['originalFailure'], rollbackFailure=event['code'])
                    self.assertTrue(oracle._tx_head(event['verifiedHead'], event['persistedFailureReceipt'], state['envelope']['prior'], lock))
                oracle.verify_transaction_case(repaired)
                with patch.object(oracle, helper, side_effect=replacement):
                    # Only a genuine full-log assertion counts, never format/scope or setup errors.
                    self.assertIsNotNone(oracle.decide_transaction(state, event, **self.options)['state']['failureReceipt'])
                    with self.assertRaisesRegex(AssertionError, 'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
                    oracle.verify_transaction_case(repaired)
                print('Task86 isolated proof omission killed:', name)

    @staticmethod
    def blocked_validator_omitting(s, contract, omission, original):
        # Trusted fixed-function substitution, no source extraction or dynamic compilation.
        # This is the BLOCKED structural branch with exactly one named predicate omitted.
        if s['phase'] != 'BLOCKED': return original(s, contract=contract)
        oracle._tx_type(s, 'State'); phases, _ = oracle._tx_scope(contract)
        if s['phase'] not in phases: raise oracle.OracleOutOfScope('Excluded transaction phase')
        if not oracle._tx_token(s['binding']) or not oracle._tx_snapshot(s['activation']): return False
        if any(s[k] is not None and not oracle._tx_match(oracle.FAILURE_CODE, s[k]) for k in ('originalFailure','rollbackFailure')): return False
        e = s['envelope']
        if not oracle._tx_envelope(e) or e['binding'] != s['binding']: return False
        if not oracle._tx_receipt(s['armCommitReceipt'], e['baseGenerationId'], e['baseGenerationSha256']): return False
        if s['activation'] != e['prior'] or s['completionReceipt'] is not None: return False
        closure = e['target'] if s['originalFailure'] is None else oracle._tx_prior_closure(e)
        if omission != 'closure' and s['pendingClosure'] is not None and s['pendingClosure'] != closure: return False
        if s['pendingClosure'] is None:
            for field in ('originalFailure','rollbackFailure'):
                if omission != 'null-pending-' + field and s[field] is None: return False
        if s['failureReceipt'] is None: return omission == 'absent-receipt-armed' or s['interlock'] == oracle._tx_armed(e)
        if omission != 'present-receipt-pending' and s['pendingClosure'] is not None: return False
        if s['originalFailure'] is None or s['rollbackFailure'] is None or not oracle._tx_follows(s, s['failureReceipt']): return False
        lock = dict(oracle._tx_armed(e), state='ROLLBACK_FAILED', originalFailure=s['originalFailure'], rollbackFailure=s['rollbackFailure'])
        if omission.startswith('lock-'): return HollowKnightSkinFailureProofGoldensTest.equal_except(s['interlock'], lock, [omission[5:]])
        return s['interlock'] == lock

    def test_proof_named_blocked_validator_omissions_kill(self):
        cases = {c['caseId']: c for c in self.proof_cases()}; original = oracle.transaction_state_valid
        panels = [('target-closure', 'closure'), ('prior-closure', 'closure'), ('null-pending-missing-originalFailure', 'null-pending-originalFailure'), ('null-pending-missing-rollbackFailure', 'null-pending-rollbackFailure'), ('null-receipt-failed-lock', 'absent-receipt-armed'), ('present-receipt-pending', 'present-receipt-pending'), ('lock-originalFailure', 'lock-originalFailure'), ('lock-rollbackFailure', 'lock-rollbackFailure')]
        probes = [(n, 'transaction_state_valid', lambda s,contract=oracle.TRANSACTION_CONTRACT,o=o: self.blocked_validator_omitting(s,contract,o,original)) for n,o in panels]
        for field in ('expectedGenerationId','expectedGenerationSha256'):
            name = 'expected-mismatch-' + field
            probes.append((name, '_tx_receipt', lambda r,g,d,n=name: self.receipt_omitting(r,g,d,n)))
        for field in ('newGenerationId','newGenerationSha256'):
            name = 'new-equals-base-' + field
            probes.append((name, '_tx_follows', lambda s,r,n=name: self.follows_omitting(s,r,n)))
        self.assertEqual(12, len(probes))
        for name, helper, replacement in probes:
            bad = cases['failure-proof-blocked-' + name + '-negative']; repaired = cases['failure-proof-blocked-' + name + '-repair']
            state = oracle._tx_case(bad)['initialState']
            with self.subTest(omission=name):
                self.assertFalse(original(state, **self.options)); oracle.verify_transaction_case(repaired)
                with patch.object(oracle, helper, side_effect=replacement):
                    self.assertTrue(oracle.transaction_state_valid(state, **self.options))
                    with self.assertRaisesRegex(AssertionError, 'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
                    oracle.verify_transaction_case(repaired)
                print('Task86 isolated BLOCKED omission killed:', name)

    def test_proof_dominated_and_overlapping_guards_not_claimed_isolated(self):
        cases = {c['caseId']: c for c in self.proof_cases()}
        for field in ('expectedGenerationId','expectedGenerationSha256'):
            name = 'syntax-' + field
            # A valid arm fixes expected receipt syntax through equality. Omitting syntax alone survives.
            with patch.object(oracle, '_tx_receipt', side_effect=lambda r,g,d,n=name: self.receipt_omitting(r,g,d,n)):
                oracle.verify_transaction_case(cases['failure-proof-' + name + '-negative'])
            oracle.verify_transaction_case(cases['failure-proof-' + name + '-repair'])
        original = oracle.transaction_state_valid
        for field in ('originalFailure','rollbackFailure'):
            bad = cases['failure-proof-blocked-present-receipt-missing-' + field + '-negative']
            # Omitting the outer null-pending condition alone still fails the inner required-failure condition.
            with patch.object(oracle, 'transaction_state_valid', side_effect=lambda s,contract=oracle.TRANSACTION_CONTRACT,f=field: self.blocked_validator_omitting(s,contract,'null-pending-' + f,original)):
                oracle.verify_transaction_case(bad)
            oracle.verify_transaction_case(cases['failure-proof-blocked-present-receipt-missing-' + field + '-repair'])
        for name in ('missing-receipt','missing-both'):
            oracle.verify_transaction_case(cases['failure-proof-' + name + '-negative'])
            oracle.verify_transaction_case(cases['failure-proof-' + name + '-repair'])

    def test_proof_recursive_nonnull_state_receipts_failures_validity_and_order(self):
        cases = oracle.load_transaction_fixture(self.fixture)
        # Existing full recursive assertion helper requires distinct rows to prove ordering.
        positive = next(c for c in cases if c['caseId'] == 'failure-proof-missing-head-repair')
        HollowKnightSkinTransactionGoldensTest.assert_transaction_leaves(self, [positive] + cases)
        pool = {}
        def collect(value):
            if isinstance(value, dict):
                for key, child in value.items():
                    if child is not None: pool[key] = copy.deepcopy(child)
                    collect(child)
            elif isinstance(value, list):
                for child in value: collect(child)
        def leaves(value, path=()):
            if isinstance(value, dict):
                for key, child in value.items(): yield from leaves(child, path + (key,))
            elif isinstance(value, list):
                for key, child in enumerate(value): yield from leaves(child, path + (key,))
            else: yield path, value
        for c in cases: collect(c['expected'])
        pool.update(originalFailure='FAILURE', rollbackFailure='ROLLBACK', failureReceipt=pool['completionReceipt'])
        for name in ('failure-proof-blocked-target-closure-negative','failure-proof-blocked-prior-closure-repair'):
            case = next(c for c in cases if c['caseId'] == name)
            # Exhaust every leaf, without claiming order sensitivity for identical terminal rows.
            for path, value in leaves(case['expected']):
                key = path[-1]
                if key == 'step': continue
                bad = copy.deepcopy(case); part = bad['expected']
                for k in path[:-1]: part = part[k]
                if key == 'type':
                    parent = bad['expected']
                    for k in path[:-2]: parent = parent[k]
                    parent[path[-2]] = {'type':'Vanilla'} if value == 'Pack' else dict(type='Pack',id='changed',treeSha256='x',contentSha256='y',importReceiptSha256='z')
                else:
                    choices = {'phase':('IDLE','ARMED'), 'state':('CLEAR','ARMED'), 'mode':('OFF','ON'), 'operation':('MODE_ON','MODE_OFF'), 'skinStamp':('7','8')}
                    part[key] = copy.deepcopy(pool[key]) if value is None else next(v for v in choices[key] if v != value) if key in choices else not value if type(value) is bool else value + 'changed'
                with self.subTest(case=name, leaf=path):
                    with self.assertRaisesRegex(AssertionError, 'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)

    def test_proof_recursive_representation_precedes_scope_and_semantics(self):
        cases = self.proof_cases(); names = ('failure-proof-missing-head-repair','failure-proof-blocked-prior-closure-repair')
        def objects(value, path=()):
            if isinstance(value, dict):
                yield path, value
                for k,v in value.items(): yield from objects(v,path+(k,))
            elif isinstance(value, list):
                for k,v in enumerate(value): yield from objects(v,path+(k,))
        for case in (c for c in cases if c['caseId'] in names):
            data = oracle._tx_case(case)
            for path, obj in objects(data):
                for damage in ('missing','extra'):
                    bad = copy.deepcopy(case); altered = copy.deepcopy(data); part = altered
                    for key in path: part = part[key]
                    if damage == 'missing': del part[next(iter(obj))]
                    else: part['unknown'] = None
                    self.reinput(bad,altered); bad['oracleContract'] = 'future'
                    with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                        with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
                        reducer.assert_not_called(); validator.assert_not_called()
            for contract in (oracle.TRANSACTION_CONTRACT, oracle.TRANSACTION_ROLLBACK_CONTRACT):
                bad = copy.deepcopy(case); bad['oracleContract'] = contract
                with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)


class HollowKnightSkinDispatchGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    semantic = staticmethod(HollowKnightSkinFailureProofGoldensTest.semantic)
    dispatch_map = """IDLE|Begin|prepare|mode-on-zero|0
IDLE|Prepared|stale-correlation|wrong-correlation-idle|0
IDLE|ArmCommitted|stale-correlation|dispatch-idle-armcommitted|0
IDLE|ApplyVerified|stale-correlation|dispatch-idle-applyverified|0
IDLE|ApplyFailed|stale-correlation|dispatch-idle-applyfailed|0
IDLE|RollbackVerified|stale-correlation|dispatch-idle-rollbackverified|0
IDLE|RollbackFailed|stale-correlation|dispatch-idle-rollbackfailed|0
IDLE|CompletionCommitted|stale-correlation|dispatch-idle-completioncommitted|0
IDLE|CompletionRejected|stale-correlation|dispatch-idle-completionrejected|0
IDLE|CompletionIndeterminate|stale-correlation|dispatch-idle-completionindeterminate|0
PREPARING|Begin|transaction-in-progress|repair-state-preparing-arm|0
PREPARING|Prepared|arm|mode-on-zero|1
PREPARING|ArmCommitted|stale-phase|dispatch-preparing-armcommitted|0
PREPARING|ApplyVerified|stale-phase|wrong-phase-preparing|0
PREPARING|ApplyFailed|stale-phase|dispatch-preparing-applyfailed|0
PREPARING|RollbackVerified|stale-phase|dispatch-preparing-rollbackverified|0
PREPARING|RollbackFailed|stale-phase|dispatch-preparing-rollbackfailed|0
PREPARING|CompletionCommitted|stale-phase|dispatch-preparing-completioncommitted|0
PREPARING|CompletionRejected|stale-phase|dispatch-preparing-completionrejected|0
PREPARING|CompletionIndeterminate|stale-phase|dispatch-preparing-completionindeterminate|0
PREPARED|Begin|transaction-in-progress|begin-in-progress|0
PREPARED|Prepared|stale-phase|wrong-phase-prepared|0
PREPARED|ArmCommitted|apply|mode-on-zero|2
PREPARED|ApplyVerified|stale-phase|dispatch-prepared-applyverified|0
PREPARED|ApplyFailed|stale-phase|rollback-phase-applyfailed-prepared|0
PREPARED|RollbackVerified|stale-phase|dispatch-prepared-rollbackverified|0
PREPARED|RollbackFailed|stale-phase|dispatch-prepared-rollbackfailed|0
PREPARED|CompletionCommitted|stale-phase|dispatch-prepared-completioncommitted|0
PREPARED|CompletionRejected|stale-phase|dispatch-prepared-completionrejected|0
PREPARED|CompletionIndeterminate|stale-phase|dispatch-prepared-completionindeterminate|0
ARMED|Begin|transaction-in-progress|repair-state-armed-clear|0
ARMED|Prepared|stale-phase|dispatch-armed-prepared|0
ARMED|ArmCommitted|stale-phase|wrong-phase-armed|0
ARMED|ApplyVerified|commit-closure|mode-on-zero|3
ARMED|ApplyFailed|rollback|rollback-history-established-pack|3
ARMED|RollbackVerified|stale-phase|rollback-phase-rollbackverified-armed|0
ARMED|RollbackFailed|stale-phase|dispatch-armed-rollbackfailed|0
ARMED|CompletionCommitted|stale-phase|dispatch-armed-completioncommitted|0
ARMED|CompletionRejected|stale-phase|dispatch-armed-completionrejected|0
ARMED|CompletionIndeterminate|stale-phase|dispatch-armed-completionindeterminate|0
APPLIED|Begin|transaction-in-progress|repair-state-applied-no-closure|0
APPLIED|Prepared|stale-phase|dispatch-applied-prepared|0
APPLIED|ArmCommitted|stale-phase|dispatch-applied-armcommitted|0
APPLIED|ApplyVerified|stale-phase|wrong-phase-applied|0
APPLIED|ApplyFailed|stale-phase|rollback-phase-applyfailed-applied|0
APPLIED|RollbackVerified|stale-phase|dispatch-applied-rollbackverified|0
APPLIED|RollbackFailed|stale-phase|dispatch-applied-rollbackfailed|0
APPLIED|CompletionCommitted|committed|mode-on-zero|4
APPLIED|CompletionRejected|rollback|failure-history-reject-restored-established-pack|4
APPLIED|CompletionIndeterminate|completion-indeterminate|failure-history-applied-indeterminate|4
ROLLBACK_PENDING|Begin|transaction-in-progress|dispatch-rollback-pending-begin|0
ROLLBACK_PENDING|Prepared|stale-phase|dispatch-rollback-pending-prepared|0
ROLLBACK_PENDING|ArmCommitted|stale-phase|dispatch-rollback-pending-armcommitted|0
ROLLBACK_PENDING|ApplyVerified|stale-phase|dispatch-rollback-pending-applyverified|0
ROLLBACK_PENDING|ApplyFailed|stale-phase|dispatch-rollback-pending-applyfailed|0
ROLLBACK_PENDING|RollbackVerified|commit-closure|rollback-history-established-pack|4
ROLLBACK_PENDING|RollbackFailed|rollback-failed|failure-history-reject-rollback-persisted|5
ROLLBACK_PENDING|CompletionCommitted|stale-phase|dispatch-rollback-pending-completioncommitted|0
ROLLBACK_PENDING|CompletionRejected|stale-phase|dispatch-rollback-pending-completionrejected|0
ROLLBACK_PENDING|CompletionIndeterminate|stale-phase|dispatch-rollback-pending-completionindeterminate|0
ROLLED_BACK|Begin|transaction-in-progress|dispatch-rolled-back-begin|0
ROLLED_BACK|Prepared|stale-phase|dispatch-rolled-back-prepared|0
ROLLED_BACK|ArmCommitted|stale-phase|dispatch-rolled-back-armcommitted|0
ROLLED_BACK|ApplyVerified|stale-phase|dispatch-rolled-back-applyverified|0
ROLLED_BACK|ApplyFailed|stale-phase|dispatch-rolled-back-applyfailed|0
ROLLED_BACK|RollbackVerified|stale-phase|rollback-phase-rollbackverified-rolled|0
ROLLED_BACK|RollbackFailed|stale-phase|dispatch-rolled-back-rollbackfailed|0
ROLLED_BACK|CompletionCommitted|committed|rollback-history-established-pack|5
ROLLED_BACK|CompletionRejected|rollback-closure-rejected|failure-history-rolled-rejected|6
ROLLED_BACK|CompletionIndeterminate|completion-indeterminate|failure-history-rolled-indeterminate|6
COMMITTED|Begin|terminal|committed-original-failure-valid|0
COMMITTED|Prepared|terminal|dispatch-committed-prepared|0
COMMITTED|ArmCommitted|terminal|dispatch-committed-armcommitted|0
COMMITTED|ApplyVerified|terminal|dispatch-committed-applyverified|0
COMMITTED|ApplyFailed|terminal|dispatch-committed-applyfailed|0
COMMITTED|RollbackVerified|terminal|dispatch-committed-rollbackverified|0
COMMITTED|RollbackFailed|terminal|dispatch-committed-rollbackfailed|0
COMMITTED|CompletionCommitted|terminal|mode-on-zero|5
COMMITTED|CompletionRejected|terminal|dispatch-committed-completionrejected|0
COMMITTED|CompletionIndeterminate|terminal|dispatch-committed-completionindeterminate|0
BLOCKED|Begin|terminal|failure-terminal-persisted|0
BLOCKED|Prepared|terminal|failure-terminal-persisted|2
BLOCKED|ArmCommitted|terminal|dispatch-blocked-armcommitted|0
BLOCKED|ApplyVerified|terminal|dispatch-blocked-applyverified|0
BLOCKED|ApplyFailed|terminal|failure-terminal-persisted|3
BLOCKED|RollbackVerified|terminal|dispatch-blocked-rollbackverified|0
BLOCKED|RollbackFailed|terminal|failure-terminal-persisted|4
BLOCKED|CompletionCommitted|terminal|dispatch-blocked-completioncommitted|0
BLOCKED|CompletionRejected|terminal|failure-terminal-persisted|5
BLOCKED|CompletionIndeterminate|terminal|failure-terminal-persisted|6"""

    def test_dispatch_corpus_and_accepted262_raw_semantic_input_log_pins(self):
        raw = self.fixture.read_bytes(); all_cases = oracle.load_transaction_fixture(self.fixture)[:318]
        self.assertEqual((318, 681), (len(all_cases), sum(len(c['expected']) for c in all_cases)))
        self.assertEqual('a898f1621701a4617279e483255de89089d2e9fc270852a399eb23e8a2b33839', hashlib.sha256(raw[:4632889]).hexdigest())
        self.assertEqual('1ebb4f52e2c11d042bae966f95d9ac8d121a12b497cfe7232d949e50920484a8', self.semantic(all_cases[:262]))
        self.assertEqual('965c51b70c1846aaabe8c5b218b6f3bcbe0e7991e1657ce96123b822e3322f08', self.semantic([c['expected'] for c in all_cases[:262]]))
        self.assertEqual('d425acb5418c4cb2b68a5fcdc195cfbd0f8c46c5ed7d595e19602a02cae4fbb7', self.semantic([c['input'] for c in all_cases[:262]]))
        added = all_cases[262:318]; self.assertEqual(56, len(added))
        for contract, count in [('transaction-forward-v1',14), ('transaction-rollback-success-v1',19), ('transaction-failure-blocked-v1',23)]:
            self.assertEqual(count, sum(c['oracleContract'] == contract for c in added))
        for case in added:
            self.assertTrue(case['caseId'].startswith('dispatch-'))
            data = oracle._tx_case(case); self.assertEqual(1, len(data['events'])); self.assertEqual(1,len(case['expected']))
            row = case['expected'][0]
            self.assertTrue(row['inputStateValid']); self.assertTrue(row['stateValid']); self.assertEqual([],row['commands']); self.assertEqual(data['initialState'],row['state'])
            oracle.verify_transaction_case(case)

    def test_dispatch_exact90_canonical_cells_and13_reused_action_witnesses(self):
        cases = {c['caseId']:c for c in oracle.load_transaction_fixture(self.fixture)}; cells=set(); actions=0
        for line in self.dispatch_map.splitlines():
            phase,event,diagnosis,name,step = line.split('|'); step=int(step)
            self.assertNotIn((phase,event),cells); cells.add((phase,event))
            self.assertIn(name,cases); case=cases[name]; data=oracle._tx_case(case)
            state=data['initialState'] if step==0 else case['expected'][step-1]['state']; row=case['expected'][step]
            self.assertEqual(phase,state['phase']); self.assertEqual(event,data['events'][step]['type'])
            self.assertTrue(row['inputStateValid']); self.assertTrue(row['stateValid']); self.assertEqual(diagnosis,row['diagnosis'])
            oracle.verify_transaction_case(case)
            if diagnosis not in ('terminal','stale-phase','stale-correlation','transaction-in-progress'):
                actions+=1; self.assertNotEqual(state,row['state'])
        phases='IDLE PREPARING PREPARED ARMED APPLIED ROLLBACK_PENDING ROLLED_BACK COMMITTED BLOCKED'.split()
        events='Begin Prepared ArmCommitted ApplyVerified ApplyFailed RollbackVerified RollbackFailed CompletionCommitted CompletionRejected CompletionIndeterminate'.split()
        self.assertEqual({(p,e) for p in phases for e in events},cells); self.assertEqual(13,actions)

    def test_dispatch_full_replay_input_immutability_and_actual_correlation(self):
        for case in oracle.load_transaction_fixture(self.fixture)[262:318]:
            before=copy.deepcopy(case); data=oracle._tx_case(case); original=copy.deepcopy(data); s=data['initialState']; e=data['events'][0]; options={'contract':case['oracleContract']}
            if e['type']!='Begin':
                self.assertRegex(e['correlation']['transactionId'],r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
                self.assertTrue(oracle._tx_token(e['correlation']['binding']))
                if s['envelope'] is not None:
                    self.assertEqual(s['envelope']['transactionId'],e['correlation']['transactionId']); self.assertEqual(s['binding'],e['correlation']['binding'])
            oracle.verify_transaction_case(case); oracle.verify_transaction_case(case)
            self.assertEqual(oracle.decide_transaction(s,e,**options),oracle.decide_transaction(s,e,**options))
            self.assertEqual(original,data); self.assertEqual(before,case)

    def test_dispatch_recursive_leaves_diagnosis_phase_and_commands_observed(self):
        cases=oracle.load_transaction_fixture(self.fixture); pool={}
        def collect(v):
            if isinstance(v,dict):
                for k,c in v.items():
                    if c is not None: pool[k]=copy.deepcopy(c)
                    collect(c)
            elif isinstance(v,list):
                for c in v: collect(c)
        def leaves(v,path=()):
            if isinstance(v,dict):
                for k,c in v.items(): yield from leaves(c,path+(k,))
            elif isinstance(v,list):
                for k,c in enumerate(v): yield from leaves(c,path+(k,))
            else: yield path,v
        for c in cases: collect(c['expected'])
        pool.update(originalFailure='FAILURE',rollbackFailure='ROLLBACK',failureReceipt=pool['completionReceipt'])
        for case in cases[262:318]:
            for path,value in leaves(case['expected']):
                key=path[-1]
                if key=='step': continue
                bad=copy.deepcopy(case); part=bad['expected']
                for k in path[:-1]: part=part[k]
                if key=='type':
                    parent=bad['expected']
                    for k in path[:-2]: parent=parent[k]
                    parent[path[-2]]={'type':'Vanilla'} if value=='Pack' else dict(type='Pack',id='changed',treeSha256='x',contentSha256='y',importReceiptSha256='z')
                else:
                    choices={'phase':('IDLE','ARMED'),'state':('CLEAR','ARMED'),'mode':('OFF','ON'),'operation':('MODE_ON','MODE_OFF'),'skinStamp':('7','8')}
                    part[key]=copy.deepcopy(pool[key]) if value is None else next(v for v in choices[key] if v!=value) if key in choices else not value if type(value) is bool else value+'changed'
                with self.subTest(case=case['caseId'],leaf=path):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
            bad=copy.deepcopy(case); bad['expected'][0]['commands']=[dict(type='Apply',correlation=copy.deepcopy(pool['correlation']))]
            with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)
        # These one-row no-ops make no ordering claim. Accepted distinct histories retain that proof.

    def test_dispatch_named_observer_and_noop_invalid_substitutions(self):
        cases=oracle.load_transaction_fixture(self.fixture); original=oracle.decide_transaction
        for case in cases[262:318]:
            for diagnosis in ('stale-phase','invalid-state'):
                with patch.object(oracle,'decide_transaction',side_effect=lambda s,e,**kw:dict(state=copy.deepcopy(s),diagnosis=diagnosis,commands=[])):
                    if case['expected'][0]['diagnosis']==diagnosis: oracle.verify_transaction_case(case)
                    else:
                        with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
            for field in ('diagnosis','phase'):
                def damaged(s,e,**kw):
                    result=original(s,e,**kw)
                    if field=='diagnosis': result['diagnosis']='observer-damaged'
                    else: result['state']['phase']='ARMED' if result['state']['phase']=='IDLE' else 'IDLE'
                    return result
                with patch.object(oracle,'decide_transaction',side_effect=damaged):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
        # Exact existing positive witnesses execute real actions, even when a command list is empty.
        byid={c['caseId']:c for c in cases}
        for line in self.dispatch_map.splitlines():
            p=line.split('|')
            if p[2] in ('terminal','stale-phase','stale-correlation','transaction-in-progress'): continue
            for diagnosis in ('stale-phase','invalid-state'):
                with patch.object(oracle,'decide_transaction',side_effect=lambda s,e,**kw:dict(state=copy.deepcopy(s),diagnosis=diagnosis,commands=[])):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(byid[p[3]])

    def test_dispatch_narrowest_contract_and_recursive_representation_precedence(self):
        def objects(value, path=()):
            if isinstance(value, dict):
                yield path, value
                for key, child in value.items(): yield from objects(child, path + (key,))
            elif isinstance(value, list):
                for key, child in enumerate(value): yield from objects(child, path + (key,))
        contracts = [oracle.TRANSACTION_CONTRACT, oracle.TRANSACTION_ROLLBACK_CONTRACT, oracle.TRANSACTION_FAILURE_CONTRACT]
        for case in oracle.load_transaction_fixture(self.fixture)[262:318]:
            data = oracle._tx_case(case)
            for narrower in contracts[:contracts.index(case['oracleContract'])]:
                bad = copy.deepcopy(case); bad['oracleContract'] = narrower
                with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                    with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)
                    reducer.assert_not_called(); validator.assert_not_called()
            for path, obj in objects(data):
                for damage in ('missing', 'extra'):
                    bad = copy.deepcopy(case); altered = copy.deepcopy(data); part = altered
                    for key in path: part = part[key]
                    if damage == 'missing': del part[next(iter(obj))]
                    else: part['unexpected'] = None
                    HollowKnightSkinTransactionGoldensTest.reinput(bad, altered); bad['oracleContract'] = 'future'
                    with self.subTest(case=case['caseId'], path=path, damage=damage):
                        with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
                            reducer.assert_not_called(); validator.assert_not_called()



class HollowKnightSkinCorrelationGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    semantic = staticmethod(HollowKnightSkinFailureProofGoldensTest.semantic)
    correlation_map = """PREPARING|Prepared|uuid-syntax|correlation-preparing-prepared-uuid-syntax|0|correlation-preparing-prepared-uuid-syntax|1|arm
PREPARING|Prepared|uuid-equality|wrong-correlation-preparing|0|mode-on-zero|1|arm
PREPARING|Prepared|token-syntax|correlation-preparing-prepared-token-syntax|0|correlation-preparing-prepared-token-syntax|1|arm
PREPARING|Prepared|token-equality|correlation-preparing-prepared-token-equality|0|correlation-preparing-prepared-token-equality|1|arm
PREPARED|ArmCommitted|uuid-syntax|correlation-prepared-armcommitted-uuid-syntax|0|correlation-prepared-armcommitted-uuid-syntax|1|apply
PREPARED|ArmCommitted|uuid-equality|correlation-prepared-armcommitted-uuid-equality|0|correlation-prepared-armcommitted-uuid-equality|1|apply
PREPARED|ArmCommitted|token-syntax|correlation-prepared-armcommitted-token-syntax|0|correlation-prepared-armcommitted-token-syntax|1|apply
PREPARED|ArmCommitted|token-equality|correlation-prepared-armcommitted-token-equality|0|correlation-prepared-armcommitted-token-equality|1|apply
ARMED|ApplyVerified|uuid-syntax|correlation-armed-applyverified-uuid-syntax|0|correlation-armed-applyverified-uuid-syntax|1|commit-closure
ARMED|ApplyVerified|uuid-equality|correlation-armed-applyverified-uuid-equality|0|correlation-armed-applyverified-uuid-equality|1|commit-closure
ARMED|ApplyVerified|token-syntax|correlation-armed-applyverified-token-syntax|0|correlation-armed-applyverified-token-syntax|1|commit-closure
ARMED|ApplyVerified|token-equality|correlation-armed-applyverified-token-equality|0|correlation-armed-applyverified-token-equality|1|commit-closure
ARMED|ApplyFailed|uuid-syntax|rollback-correlation-applyfailed-uuid-syntax|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|uuid-equality|rollback-correlation-applyfailed-uuid-mismatch|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|token-syntax|rollback-correlation-applyfailed-token-syntax|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|token-equality|rollback-correlation-applyfailed-token-mismatch|0|rollback-history-unestablished-pack|3|rollback
ROLLBACK_PENDING|RollbackVerified|uuid-syntax|rollback-correlation-rollbackverified-uuid-syntax|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|uuid-equality|rollback-correlation-rollbackverified-uuid-mismatch|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|token-syntax|rollback-correlation-rollbackverified-token-syntax|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|token-equality|rollback-correlation-rollbackverified-token-mismatch|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackFailed|uuid-syntax|failure-correlation-rollbackfailed|0|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|uuid-equality|failure-correlation-rollbackfailed|1|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|token-syntax|failure-correlation-rollbackfailed|2|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|token-equality|failure-correlation-rollbackfailed|3|failure-history-reject-rollback-unpersisted|5|rollback-failed
APPLIED|CompletionCommitted|uuid-syntax|correlation-applied-completioncommitted-uuid-syntax|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|uuid-equality|correlation-applied-completioncommitted-uuid-equality|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|token-syntax|correlation-applied-completioncommitted-token-syntax|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|token-equality|correlation-applied-completioncommitted-token-equality|0|correlation-applied-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|uuid-syntax|correlation-rolled-back-completioncommitted-uuid-syntax|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|uuid-equality|correlation-rolled-back-completioncommitted-uuid-equality|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|token-syntax|correlation-rolled-back-completioncommitted-token-syntax|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|token-equality|correlation-rolled-back-completioncommitted-token-equality|0|correlation-rolled-back-completioncommitted-repair|0|committed
APPLIED|CompletionRejected|uuid-syntax|failure-correlation-applied-rejected|0|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|uuid-equality|failure-correlation-applied-rejected|1|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|token-syntax|failure-correlation-applied-rejected|2|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|token-equality|failure-correlation-applied-rejected|3|failure-history-reject-restored-unestablished-pack|4|rollback
ROLLED_BACK|CompletionRejected|uuid-syntax|failure-correlation-rolled-rejected|0|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|uuid-equality|failure-correlation-rolled-rejected|1|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|token-syntax|failure-correlation-rolled-rejected|2|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|token-equality|failure-correlation-rolled-rejected|3|failure-history-rolled-rejected|6|rollback-closure-rejected
APPLIED|CompletionIndeterminate|uuid-syntax|failure-correlation-applied-indeterminate|0|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|uuid-equality|failure-correlation-applied-indeterminate|1|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|token-syntax|failure-correlation-applied-indeterminate|2|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|token-equality|failure-correlation-applied-indeterminate|3|failure-history-applied-indeterminate|4|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|uuid-syntax|failure-correlation-rolled-indeterminate|0|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|uuid-equality|failure-correlation-rolled-indeterminate|1|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|token-syntax|failure-correlation-rolled-indeterminate|2|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|token-equality|failure-correlation-rolled-indeterminate|3|failure-history-rolled-indeterminate|6|completion-indeterminate"""

    def test_correlation_corpus_and_accepted318_pins(self):
        cases=oracle.load_transaction_fixture(self.fixture)[:339]; old=cases[:318]
        self.assertEqual((339,713),(len(cases),sum(len(c['expected']) for c in cases)))
        self.assertEqual((21,32),(len(cases[318:339]),sum(len(c['expected']) for c in cases[318:339])))
        self.assertEqual('e346800246c19ef81d59174bc0ad29af1ccd5c1de09e9addb3080511d5a2ec78',hashlib.sha256(self.fixture.read_bytes()[:5014066]).hexdigest())
        self.assertEqual('923a1ff2e830cc8b86c00cae28ebc4f565a30b2d2890b8c56c5e382a80f9e6d0',self.semantic(old)); self.assertEqual('eb8b5ef66678709bd81405674db2775cd3d57ac55984e86361067a34a9b19742',self.semantic([c['expected'] for c in old])); self.assertEqual('afb867646a0597ebc63243aedc699cfa919fd452ed67bace69f414eda3f95c15',self.semantic([c['input'] for c in old]))
        for contract,count,steps in [('transaction-forward-v1',112,205),('transaction-rollback-success-v1',57,101),('transaction-failure-blocked-v1',170,407)]:
            partition=[c for c in cases if c['oracleContract']==contract]; self.assertEqual((count,steps),(len(partition),sum(len(c['expected']) for c in partition)))
        for case in cases: oracle.verify_transaction_case(case)

    @staticmethod
    def witness(cases,name,step):
        case=cases[name]; data=oracle._tx_case(case); step=int(step)
        return case,data['initialState'] if step==0 else case['expected'][step-1]['state'],data['events'][step],case['expected'][step]

    def test_correlation_exact48_axes_and_original_snapshot_positive_proof(self):
        cases={c['caseId']:c for c in oracle.load_transaction_fixture(self.fixture)}; cells=set(); reused=0
        for line in self.correlation_map.splitlines():
            phase,event,axis,name,step,positive,positive_step,diagnosis=line.split('|')
            self.assertNotIn((phase,event,axis),cells); cells.add((phase,event,axis))
            case,s,e,row=self.witness(cases,name,step); pc,ps,pe,pr=self.witness(cases,positive,positive_step); c=e['correlation']
            self.assertEqual((phase,event),(s['phase'],e['type'])); self.assertTrue(oracle.transaction_state_valid(s,contract=case['oracleContract']))
            self.assertTrue(row['inputStateValid']); self.assertTrue(row['stateValid']); self.assertEqual('stale-correlation',row['diagnosis']); self.assertEqual(s,row['state']); self.assertEqual([],row['commands'])
            syntax=axis.endswith('-syntax')
            if axis.startswith('uuid-'):
                self.assertEqual(s['binding'],c['binding']); self.assertNotEqual(s['envelope']['transactionId'],c['transactionId']); self.assertEqual(not syntax,oracle._tx_match(oracle.UUID,c['transactionId']))
            else:
                self.assertEqual(s['envelope']['transactionId'],c['transactionId']); self.assertNotEqual(s['binding'],c['binding']); self.assertEqual(not syntax,oracle._tx_token(c['binding']))
            # Repairs change exactly one identity component, not receipt/code/phase prerequisites.
            self.assertEqual(s,ps); repaired=copy.deepcopy(e); repaired['correlation']=dict(transactionId=s['envelope']['transactionId'],binding=copy.deepcopy(s['binding'])); self.assertEqual(repaired,pe)
            self.assertTrue(pr['inputStateValid']); self.assertTrue(pr['stateValid']); self.assertEqual(diagnosis,pr['diagnosis']); self.assertNotEqual(s,pr['state'])
            oracle.verify_transaction_case(case); oracle.verify_transaction_case(pc)
            actual=oracle.decide_transaction(copy.deepcopy(s),copy.deepcopy(pe),contract=pc['oracleContract']); self.assertEqual({k:pr[k] for k in ('state','diagnosis','commands')},actual)
            if not name.startswith('correlation-'): reused+=1
            elif event=='CompletionCommitted': self.assertNotEqual(name,positive); self.assertEqual(1,len(case['expected'])); self.assertEqual('0',positive_step)
            else: self.assertEqual(name,positive); self.assertEqual('1',positive_step)
        contexts=[('PREPARING','Prepared'),('PREPARED','ArmCommitted'),('ARMED','ApplyVerified'),('ARMED','ApplyFailed'),('ROLLBACK_PENDING','RollbackVerified'),('ROLLBACK_PENDING','RollbackFailed')]+[(p,e) for p in ('APPLIED','ROLLED_BACK') for e in ('CompletionCommitted','CompletionRejected','CompletionIndeterminate')]
        self.assertEqual({(p,e,a) for p,e in contexts for a in ('uuid-syntax','uuid-equality','token-syntax','token-equality')},cells); self.assertEqual(29,reused)

    def test_correlation_full_replay_and_input_immutability(self):
        for case in oracle.load_transaction_fixture(self.fixture)[318:339]:
            before=copy.deepcopy(case); oracle.verify_transaction_case(case); oracle.verify_transaction_case(case); self.assertEqual(before,case)
            data=oracle._tx_case(case); original=copy.deepcopy(data); s=data['initialState']
            for event,row in zip(data['events'],case['expected']):
                a=oracle.decide_transaction(s,event,contract=case['oracleContract']); b=oracle.decide_transaction(s,event,contract=case['oracleContract']); self.assertEqual(a,b); s=a['state']
            self.assertEqual(original,data)

    def test_correlation_named_diagnosis_phase_noop_invalid_and_order_observers(self):
        original=oracle.decide_transaction
        for case in oracle.load_transaction_fixture(self.fixture)[318:339]:
            for diagnosis in ('stale-phase','invalid-state'):
                with patch.object(oracle,'decide_transaction',side_effect=lambda s,e,**kw:dict(state=copy.deepcopy(s),diagnosis=diagnosis,commands=[])):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
            for field in ('diagnosis','phase'):
                def damaged(s,e,**kw):
                    result=original(s,e,**kw)
                    if field=='diagnosis': result['diagnosis']='observer-damaged'
                    else: result['state']['phase']='IDLE'
                    return result
                with patch.object(oracle,'decide_transaction',side_effect=damaged):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
            if len(case['expected'])==2:
                bad=copy.deepcopy(case);bad['expected'].reverse()
                for i,row in enumerate(bad['expected']):row['step']=i
                with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(bad)
        # Invalid syntax necessarily also mismatches identity; no isolated syntax omission claim.
    def test_correlation_recursive_full_state_command_and_validity_leaves(self):
        cases=oracle.load_transaction_fixture(self.fixture); pool={}
        def collect(v):
            if isinstance(v,dict):
                for k,c in v.items():
                    if c is not None: pool[k]=copy.deepcopy(c)
                    collect(c)
            elif isinstance(v,list):
                for c in v: collect(c)
        def leaves(v,path=()):
            if isinstance(v,dict):
                for k,c in v.items(): yield from leaves(c,path+(k,))
            elif isinstance(v,list):
                for k,c in enumerate(v): yield from leaves(c,path+(k,))
            else: yield path,v
        for c in cases: collect(c['expected'])
        pool.update(originalFailure='FAILURE',rollbackFailure='ROLLBACK',failureReceipt=pool['completionReceipt'])
        for case in cases[318:339]:
            for path,value in leaves(case['expected']):
                key=path[-1]
                if key=='step': continue
                bad=copy.deepcopy(case); part=bad['expected']
                for k in path[:-1]: part=part[k]
                if key=='type':
                    parent=bad['expected']
                    for k in path[:-2]: parent=parent[k]
                    parent[path[-2]]={'type':'Vanilla'} if value=='Pack' else dict(type='Pack',id='changed',treeSha256='x',contentSha256='y',importReceiptSha256='z') if value=='Vanilla' else dict(type='Rollback' if value=='Apply' else 'Apply',correlation=copy.deepcopy(pool['correlation']))
                else:
                    choices={'phase':('IDLE','ARMED'),'state':('CLEAR','ARMED'),'mode':('OFF','ON'),'operation':('MODE_ON','MODE_OFF'),'skinStamp':('7','8')}
                    part[key]=copy.deepcopy(pool[key]) if value is None else next(v for v in choices[key] if v!=value) if key in choices else not value if type(value) is bool else value+'changed'
                with self.subTest(case=case['caseId'],leaf=path):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
            bad=copy.deepcopy(case); bad['expected'][0]['commands']=[dict(type='Apply',correlation=copy.deepcopy(pool['correlation']))]
            with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)
        # Separate terminal repairs have one row; distinct inline bad/repaired rows prove order in the named observer test.

    def test_correlation_narrowest_contract_recursive_representation_precedence(self):
        def objects(value, path=()):
            if isinstance(value, dict):
                yield path, value
                for key, child in value.items(): yield from objects(child, path + (key,))
            elif isinstance(value, list):
                for key, child in enumerate(value): yield from objects(child, path + (key,))
        contracts = [oracle.TRANSACTION_CONTRACT, oracle.TRANSACTION_ROLLBACK_CONTRACT, oracle.TRANSACTION_FAILURE_CONTRACT]
        for case in oracle.load_transaction_fixture(self.fixture)[318:339]:
            data = oracle._tx_case(case)
            for narrower in contracts[:contracts.index(case['oracleContract'])]:
                bad = copy.deepcopy(case); bad['oracleContract'] = narrower
                with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                    with self.assertRaises(oracle.OracleOutOfScope): oracle.verify_transaction_case(bad)
                    reducer.assert_not_called(); validator.assert_not_called()
            for path, obj in objects(data):
                for damage in ('missing', 'extra'):
                    bad = copy.deepcopy(case); altered = copy.deepcopy(data); part = altered
                    for key in path: part = part[key]
                    if damage == 'missing': del part[next(iter(obj))]
                    else: part['unexpected'] = None
                    HollowKnightSkinTransactionGoldensTest.reinput(bad, altered); bad['oracleContract'] = 'future'
                    with self.subTest(case=case['caseId'], path=path, damage=damage):
                        with patch.object(oracle, 'decide_transaction') as reducer, patch.object(oracle, 'transaction_state_valid') as validator:
                            with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
                            reducer.assert_not_called(); validator.assert_not_called()




class HollowKnightSkinCodeLexicalGoldensTest(unittest.TestCase):
    # Task89: applicable-state code lexical panel only, not correlation/state/precedence closure.
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    semantic = staticmethod(HollowKnightSkinFailureProofGoldensTest.semantic)
    negative = ['0A','_A','@A','[A','Aa','A/','A:','A[','A^','A`','A-',
                ' A','A ','A A','A\0','A\t','A\n','A\r','A\x7f','A\x9f',
                'Ａ','Aé','A\U0001f600','A\u202e']
    contexts = [('applyfailed','rollback-code-empty','ARMED','ApplyFailed','rollback'),
                ('rollbackfailed','failure-code-rollbackfailed','ROLLBACK_PENDING','RollbackFailed','rollback-failed'),
                ('applied-rejected','failure-code-applied-rejected','APPLIED','CompletionRejected','rollback'),
                ('rolled-rejected','failure-code-rolled-rejected','ROLLED_BACK','CompletionRejected','rollback-closure-rejected')]
    suffixes = ['negative-panel-and-min-repair','uppercase-last','min-plus-one','body-classes','max-minus-one']

    def code_cases(self):
        return oracle.load_transaction_fixture(self.fixture)[339:359]

    def test_code_corpus_and_accepted339_pins(self):
        cases=oracle.load_transaction_fixture(self.fixture)[:359]; old=cases[:339]
        self.assertEqual((359,829),(len(cases),sum(len(c['expected']) for c in cases)))
        self.assertEqual((20,116),(len(cases[339:]),sum(len(c['expected']) for c in cases[339:])))
        raw=self.fixture.read_bytes(); suffix=b'\r\n  ]\r\n}\r\n'
        self.assertEqual('25d9edf7294e4f53bbed4fbc9b5579fac9db93e873edbc4d8d478c32f976047c',hashlib.sha256(raw[:5228164]).hexdigest())
        self.assertEqual('abd80e5eb9320ae21f00ac7f1527b271545d8723f4cd610dbf7ad82ec538d683',hashlib.sha256(raw[:5228164]+suffix).hexdigest())
        self.assertEqual('3eba074572588742df14b9127d3ab8df9c3796b59c29d75b3beb8f69bfd10317',self.semantic(old))
        self.assertEqual('8aa78e2234b6141850627467fc5753a47751e8b7e8165e1fdada11976e0a3e69',self.semantic([c['input'] for c in old]))
        self.assertEqual('afdcc4b754dd722f9890366916f74dbf180d176b2b904eeb93ad374f5d800159',self.semantic([c['expected'] for c in old]))
        for contract,count,steps in [('transaction-forward-v1',112,205),('transaction-rollback-success-v1',62,130),('transaction-failure-blocked-v1',185,494)]:
            part=[c for c in cases if c['oracleContract']==contract]
            self.assertEqual((count,steps),(len(part),sum(len(c['expected']) for c in part)))
        for case in cases[339:]: oracle.verify_transaction_case(case)

    def test_code_exact_panels_and_original_snapshot_repairs(self):
        all_cases={c['caseId']:c for c in oracle.load_transaction_fixture(self.fixture)}
        self.assertEqual(24,len(self.negative)); witnessed=set()
        for context,seed,phase,event,diagnosis in self.contexts:
            initial=oracle._tx_case(all_cases[seed]); s=initial['initialState']; original=initial['events'][0]
            for suffix,codes in zip(self.suffixes,[self.negative+['A'],['Z'],['A0'],['AZ09_'],['A'*127]]):
                case=all_cases['lex-code-'+context+'-'+suffix]; witnessed.add(case['caseId']); d=oracle._tx_case(case)
                self.assertEqual(s,d['initialState']); self.assertEqual(phase,s['phase']); self.assertEqual(codes,[e['code'] for e in d['events']])
                self.assertEqual('transaction-rollback-success-v1' if context=='applyfailed' else 'transaction-failure-blocked-v1',case['oracleContract'])
                options={'contract':case['oracleContract']}; self.assertTrue(oracle.transaction_state_valid(s,**options))
                for i,(ev,row) in enumerate(zip(d['events'],case['expected'])):
                    with self.subTest(context=context,suffix=suffix,step=i):
                        self.assertEqual(dict(original,code=codes[i]),ev); self.assertEqual(event,ev['type'])
                        self.assertEqual(s['binding'],ev['correlation']['binding']); self.assertEqual(s['envelope']['transactionId'],ev['correlation']['transactionId'])
                        self.assertTrue(row['inputStateValid']); self.assertTrue(row['stateValid'])
                        if suffix==self.suffixes[0] and i<24:
                            self.assertEqual('invalid-code',row['diagnosis']); self.assertEqual(s,row['state']); self.assertEqual([],row['commands'])
                            repaired=dict(ev,code='A'); self.assertEqual(d['events'][24],repaired)
                            positive=oracle.decide_transaction(copy.deepcopy(s),repaired,**options)
                            self.assertEqual({k:case['expected'][24][k] for k in ('state','diagnosis','commands')},positive)
                        else:
                            expected=copy.deepcopy(s)
                            if diagnosis=='rollback': expected.update(phase='ROLLBACK_PENDING',pendingClosure=None,originalFailure=codes[i])
                            else: expected.update(phase='BLOCKED',rollbackFailure=codes[i])
                            commands=[dict(type='Rollback',correlation=copy.deepcopy(ev['correlation']))] if diagnosis=='rollback' else []
                            self.assertEqual(expected,row['state']); self.assertEqual(diagnosis,row['diagnosis']); self.assertEqual(commands,row['commands']); self.assertNotEqual(s,row['state'])
                oracle.verify_transaction_case(case)
        self.assertEqual(20,len(witnessed))
        # Existing empty/lower/129/128 boundaries remain exact; no duplicate golden cases.
        for context,seed,_,_,_ in self.contexts:
            if context=='applyfailed':
                names=['rollback-code-empty','rollback-code-lower','rollback-code-over128','rollback-code-max128']
                self.assertEqual(['','lower','A'*129,'A'*128],[oracle._tx_case(all_cases[n])['events'][0]['code'] for n in names])
                for n in names: oracle.verify_transaction_case(all_cases[n])
            else:
                self.assertEqual(['','lower','A'*129,'A'*128],[e['code'] for e in oracle._tx_case(all_cases[seed])['events']]); oracle.verify_transaction_case(all_cases[seed])

    def test_code_full_replay_and_input_immutability(self):
        for case in self.code_cases():
            before=copy.deepcopy(case); oracle.verify_transaction_case(case); oracle.verify_transaction_case(case); self.assertEqual(before,case)
            d=oracle._tx_case(case); original=copy.deepcopy(d); s=d['initialState']
            for ev in d['events']:
                a=oracle.decide_transaction(s,ev,contract=case['oracleContract']); b=oracle.decide_transaction(s,ev,contract=case['oracleContract']); self.assertEqual(a,b); s=a['state']
            self.assertEqual(original,d)

    def test_code_named32_malformed_code_controls_precede_reducer(self):
        cases={c['caseId']:c for c in self.code_cases()}; controls=set()
        damages=[('null',None),('bool',True),('integer',0),('array',[]),('object',{}),('missing',None),('high','\ud800'),('low','\udc00')]
        for context,_,_,_,_ in self.contexts:
            base=cases['lex-code-'+context+'-uppercase-last']
            for label,value in damages:
                bad=copy.deepcopy(base); d=oracle._tx_case(bad)
                if label=='missing': del d['events'][0]['code']
                else: d['events'][0]['code']=value
                raw=json.dumps(d,ensure_ascii=True,separators=(',',':')).encode('utf8'); bad['input']={'utf8Text':raw.decode('utf8')}; bad['inputSha256']=hashlib.sha256(raw).hexdigest()
                with self.subTest(context=context,damage=label), patch.object(oracle,'decide_transaction') as reducer, patch.object(oracle,'transaction_state_valid') as validator:
                    with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
                    reducer.assert_not_called(); validator.assert_not_called()
                controls.add((context,label))
        self.assertEqual(32,len(controls))
        # Escaped LF/NUL/control strings are semantic invalid-code, not malformed Unicode.
        for c in self.code_cases(): oracle.verify_transaction_case(c)

    def test_code_named_validity_diagnosis_phase_commands_and_order_observers(self):
        original=oracle.decide_transaction
        for case in self.code_cases():
            for diagnosis in ('stale-phase','invalid-state'):
                with patch.object(oracle,'decide_transaction',side_effect=lambda s,e,**kw:dict(state=copy.deepcopy(s),diagnosis=diagnosis,commands=[])):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
            with patch.object(oracle,'transaction_state_valid',return_value=False):
                with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
            for field in ('diagnosis','phase','commands'):
                def damaged(s,e,**kw):
                    result=original(s,e,**kw)
                    if field=='diagnosis': result['diagnosis']='observer-damaged'
                    elif field=='phase': result['state']['phase']='IDLE'
                    else: result['commands']=[dict(type='Apply',correlation=copy.deepcopy(e['correlation']))]
                    return result
                with patch.object(oracle,'decide_transaction',side_effect=damaged):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(case)
            if len(case['expected'])==25:
                bad=copy.deepcopy(case); bad['expected'][0],bad['expected'][24]=bad['expected'][24],bad['expected'][0]
                for i,row in enumerate(bad['expected']): row['step']=i
                with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
                # The24 negative rows are identical except step; only final positive distinguishes order.
            for i,row in enumerate(case['expected']):
                bad=copy.deepcopy(case); del bad['expected'][i]
                with self.assertRaises(oracle.FixtureFormatError): oracle.verify_transaction_case(bad)
                if row['commands']:
                    for commands in ([],row['commands']*2):
                        bad=copy.deepcopy(case);bad['expected'][i]['commands']=copy.deepcopy(commands)
                        with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(bad)
        # Zero/one commands do not prove ordering among multiple distinct commands.

    def test_code_narrowest_contracts_and_default_api_preserved(self):
        contracts=[oracle.TRANSACTION_CONTRACT,oracle.TRANSACTION_ROLLBACK_CONTRACT,oracle.TRANSACTION_FAILURE_CONTRACT]
        for case in self.code_cases():
            data=oracle._tx_case(case)
            for contract in contracts[:contracts.index(case['oracleContract'])]:
                bad=copy.deepcopy(case);bad['oracleContract']=contract
                with patch.object(oracle,'decide_transaction') as reducer,patch.object(oracle,'transaction_state_valid') as validator:
                    with self.assertRaises(oracle.OracleOutOfScope):oracle.verify_transaction_case(bad)
                    reducer.assert_not_called();validator.assert_not_called()
            with self.assertRaises(oracle.OracleOutOfScope):oracle.decide_transaction(data['initialState'],data['events'][0])

    def _assert_code_recursive_full_state_command_and_validity_leaves(self, context):
        cases=oracle.load_transaction_fixture(self.fixture); pool={}
        def collect(v):
            if isinstance(v,dict):
                for k,c in v.items():
                    if c is not None: pool[k]=copy.deepcopy(c)
                    collect(c)
            elif isinstance(v,list):
                for c in v: collect(c)
        def leaves(v,path=()):
            if isinstance(v,dict):
                for k,c in v.items(): yield from leaves(c,path+(k,))
            elif isinstance(v,list):
                for k,c in enumerate(v): yield from leaves(c,path+(k,))
            else: yield path,v
        for c in cases: collect(c['expected'])
        pool.update(originalFailure='FAILURE',rollbackFailure='ROLLBACK',failureReceipt=pool['completionReceipt'])
        for case in cases[339:359]:
            if not case['caseId'].startswith('lex-code-'+context+'-'): continue
            for path,value in leaves(case['expected']):
                key=path[-1]
                if key=='step': continue
                bad=copy.deepcopy(case); part=bad['expected']
                for k in path[:-1]: part=part[k]
                if key=='type':
                    parent=bad['expected']
                    for k in path[:-2]: parent=parent[k]
                    parent[path[-2]]={'type':'Vanilla'} if value=='Pack' else dict(type='Pack',id='changed',treeSha256='x',contentSha256='y',importReceiptSha256='z') if value=='Vanilla' else dict(type='Rollback' if value=='Apply' else 'Apply',correlation=copy.deepcopy(pool['correlation']))
                else:
                    choices={'phase':('IDLE','ARMED'),'state':('CLEAR','ARMED'),'mode':('OFF','ON'),'operation':('MODE_ON','MODE_OFF'),'skinStamp':('7','8')}
                    part[key]=copy.deepcopy(pool[key]) if value is None else next(v for v in choices[key] if v!=value) if key in choices else not value if type(value) is bool else value+'changed'
                with self.subTest(case=case['caseId'],leaf=path):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
            bad=copy.deepcopy(case); bad['expected'][0]['commands']=[dict(type='Apply',correlation=copy.deepcopy(pool['correlation']))]
            with self.assertRaises(AssertionError): oracle.verify_transaction_case(bad)
        # Separate terminal repairs have one row; distinct inline bad/repaired rows prove order in the named observer test.




class HollowKnightSkinCodeApplyFailedRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_code_applyfailed_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinCodeLexicalGoldensTest._assert_code_recursive_full_state_command_and_validity_leaves(self, 'applyfailed')


class HollowKnightSkinCodeRollbackFailedRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_code_rollbackfailed_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinCodeLexicalGoldensTest._assert_code_recursive_full_state_command_and_validity_leaves(self, 'rollbackfailed')


class HollowKnightSkinCodeAppliedRejectedRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_code_applied_rejected_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinCodeLexicalGoldensTest._assert_code_recursive_full_state_command_and_validity_leaves(self, 'applied-rejected')


class HollowKnightSkinCodeRolledRejectedRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_code_rolled_rejected_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinCodeLexicalGoldensTest._assert_code_recursive_full_state_command_and_validity_leaves(self, 'rolled-rejected')




class HollowKnightSkinUuidLexicalGoldensTest(unittest.TestCase):
    # Representative UUID-only strings; syntax also mismatches identity. No RFC semantics.
    fixture = HollowKnightSkinTransactionGoldensTest.fixture
    semantic = staticmethod(HollowKnightSkinFailureProofGoldensTest.semantic)
    panels = [('shape', ['', '11111111-1111-1111-1111-11111111111', '11111111-1111-1111-1111-1111111111111', '111111111111-1111-1111-111111111111', '11111111--1111-1111-1111-111111111111', '1111111-11111-1111-1111-111111111111', '11111111_1111-1111-1111-111111111111', '11111111111111111111111111111111', '{11111111-1111-1111-1111-111111111111}', 'urn:uuid:11111111-1111-1111-1111-111111111111']), ('ascii', ['A1111111-1111-1111-1111-111111111111', 'F1111111-1111-1111-1111-111111111111', 'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA', '/1111111-1111-1111-1111-111111111111', ':1111111-1111-1111-1111-111111111111', '`1111111-1111-1111-1111-111111111111', 'g1111111-1111-1111-1111-111111111111', 'G1111111-1111-1111-1111-111111111111']), ('whitespace', [' 11111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111 ', '1111 111-1111-1111-1111-111111111111', '\t11111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111\t', '1111\t111-1111-1111-1111-111111111111', '\n11111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111\n', '1111\n111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111\r', '11111111-1111-1111-1111-111111111111\r\n', '1111\x00111-1111-1111-1111-111111111111', '1111\x7f111-1111-1111-1111-111111111111', '1111\x85111-1111-1111-1111-111111111111', '\xa011111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111\xa0', '1111\u2028111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111\u2028']), ('unicode', ['\u06601111111-1111-1111-1111-111111111111', '\uff101111111-1111-1111-1111-111111111111', '\uff411111111-1111-1111-1111-111111111111', '\u03b11111111-1111-1111-1111-111111111111', '\u04301111111-1111-1111-1111-111111111111', '11111111\u20101111-1111-1111-111111111111', '\U0001f6001111111-1111-1111-1111-111111111111', '\U0001d7ce1111111-1111-1111-1111-111111111111'])]
    positives = [('zero', '00000000-0000-0000-0000-000000000000'), ('nine', '99999999-9999-9999-9999-999999999999'), ('a', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'), ('f', 'ffffffff-ffff-ffff-ffff-ffffffffffff')]
    donor = 'correlation-preparing-prepared-uuid-syntax'

    def uuid_cases(self):
        return oracle.load_transaction_fixture(self.fixture)[359:367]

    def test_uuid_corpus_and_accepted359_pins(self):
        cases=oracle.load_transaction_fixture(self.fixture); old=cases[:359]
        self.assertEqual((367,881),(len(cases),sum(len(c['expected']) for c in cases)))
        self.assertEqual((8,52),(len(cases[359:]),sum(len(c['expected']) for c in cases[359:])))
        raw=self.fixture.read_bytes(); suffix=b'\r\n  ]\r\n}\r\n'
        self.assertEqual('88dc3230cbe4fc346120b26c003aa2e716d71d3ca7b27f2eb66098d9716c5b2d',hashlib.sha256(raw[:5937735]).hexdigest())
        self.assertEqual('e9b0d6eef5b2fb03cc4156b97c2ced01e31a1ec71dbdc015fe6d1be5bbaf896e',hashlib.sha256(raw[:5937735]+suffix).hexdigest())
        self.assertEqual('7bdd4fb5f85a41fecb8d71e642e56875a4ff95cfb209b97fc00338217e37e5cd',self.semantic(old))
        self.assertEqual('50ead1606cd504bfadd72bfaceef6b92bc0a43db841822c7089c272b2bcd9502',self.semantic([c['input'] for c in old]))
        self.assertEqual('db3564f733657d9d527f072ef92f82afc1e0d3d866970051d0df65b28500d7dc',self.semantic([c['expected'] for c in old]))
        for contract,count,steps in [('transaction-forward-v1',120,257),('transaction-rollback-success-v1',62,130),('transaction-failure-blocked-v1',185,494)]:
            part=[c for c in cases if c['oracleContract']==contract]
            self.assertEqual((count,steps),(len(part),sum(len(c['expected']) for c in part)))
        for case in cases: oracle.verify_transaction_case(case)

    def test_uuid_exact_panels_original_snapshot_repairs_and_coherent_positives(self):
        all_cases={c['caseId']:c for c in oracle.load_transaction_fixture(self.fixture)}
        donor=oracle._tx_case(all_cases[self.donor]); original=donor['events'][1]; original_state=donor['initialState']; count=0; negatives=0
        panels=[(name,values+[original['correlation']['transactionId']],False) for name,values in self.panels]+[('positive-'+name,[value],True) for name,value in self.positives]
        for name,values,positive in panels:
            case=all_cases['lex-uuid-preparing-prepared-'+name]; data=oracle._tx_case(case); s=data['initialState']; expected_initial=copy.deepcopy(original_state)
            if positive: expected_initial['envelope']['transactionId']=values[0]
            self.assertEqual(expected_initial,s); self.assertTrue(oracle.transaction_state_valid(s)); self.assertEqual('transaction-forward-v1',case['oracleContract'])
            self.assertEqual(values,[e['correlation']['transactionId'] for e in data['events']])
            for i,(ev,row) in enumerate(zip(data['events'],case['expected'])):
                expected_event=copy.deepcopy(original); expected_event['correlation']['transactionId']=values[i]; self.assertEqual(expected_event,ev)
                self.assertTrue(row['inputStateValid']); self.assertTrue(row['stateValid']); out=copy.deepcopy(s)
                if not positive and i<len(values)-1:
                    negatives+=1; self.assertEqual(s,row['state']); self.assertEqual('stale-correlation',row['diagnosis']); self.assertEqual([],row['commands'])
                    repair=copy.deepcopy(ev); repair['correlation']['transactionId']=original['correlation']['transactionId']; self.assertEqual(original,repair)
                    result=oracle.decide_transaction(copy.deepcopy(original_state),repair)
                    repaired_state=copy.deepcopy(original_state); repaired_state['phase']='PREPARED'
                    self.assertEqual(dict(state=repaired_state,diagnosis='arm',commands=[dict(type='Arm',envelope=copy.deepcopy(original_state['envelope']))]),result)
                else:
                    out['phase']='PREPARED'; self.assertEqual(out,row['state']); self.assertEqual('arm',row['diagnosis'])
                    self.assertEqual([dict(type='Arm',envelope=copy.deepcopy(s['envelope']))],row['commands'])
            oracle.verify_transaction_case(case); count+=1
        self.assertEqual((8,44),(count,negatives))

    def test_uuid_full_replay_and_input_immutability(self):
        for case in self.uuid_cases():
            before=copy.deepcopy(case); oracle.verify_transaction_case(case); oracle.verify_transaction_case(case); self.assertEqual(before,case)
            data=oracle._tx_case(case); original=copy.deepcopy(data); s=data['initialState']
            for event in data['events']:
                a=oracle.decide_transaction(s,event); b=oracle.decide_transaction(s,event); self.assertEqual(a,b); s=a['state']
            self.assertEqual(original,data)

    def test_uuid_named_representation_scope_and_default_contract_controls(self):
        cases=oracle.load_transaction_fixture(self.fixture); base=next(c for c in cases if c['caseId']==self.donor); controls=set()
        def reinput(case,data):
            text=json.dumps(data,ensure_ascii=True,separators=(',',':')); case['input']={'utf8Text':text}; case['inputSha256']=hashlib.sha256(text.encode()).hexdigest()
        damages=[('null',None),('bool',True),('integer',1),('array',[]),('object',{}),('missing',None),('high','\ud800'),('low','\udc00'),('reversed','\udc00\ud800')]
        for name,value in damages:
            bad=copy.deepcopy(base); data=oracle._tx_case(bad)
            if name=='missing': del data['events'][0]['correlation']['transactionId']
            else: data['events'][0]['correlation']['transactionId']=value
            reinput(bad,data)
            with patch.object(oracle,'decide_transaction') as reducer,patch.object(oracle,'transaction_state_valid') as validator:
                with self.assertRaises(oracle.FixtureFormatError):oracle.verify_transaction_case(bad)
                reducer.assert_not_called();validator.assert_not_called()
            controls.add('Prepared|'+name)
        for kind in ('ApplyFailed','RollbackVerified','RollbackFailed','CompletionRejected','CompletionIndeterminate'):
            seed=next(e for c in cases for e in oracle._tx_case(c)['events'] if e['type']==kind)
            for malformed in (False,True):
                bad=copy.deepcopy(base); data=oracle._tx_case(bad); event=copy.deepcopy(seed); event['correlation']['transactionId']=None if malformed else ''; data['events'][0]=event; reinput(bad,data)
                with patch.object(oracle,'decide_transaction') as reducer,patch.object(oracle,'transaction_state_valid') as validator:
                    with self.assertRaises(oracle.FixtureFormatError if malformed else oracle.OracleOutOfScope):oracle.verify_transaction_case(bad)
                    reducer.assert_not_called();validator.assert_not_called()
                controls.add(kind+'|'+str(malformed))
        self.assertEqual(19,len(controls))
        for case in self.uuid_cases():
            data=oracle._tx_case(case)
            for contract in (oracle.TRANSACTION_CONTRACT,oracle.TRANSACTION_ROLLBACK_CONTRACT,oracle.TRANSACTION_FAILURE_CONTRACT):
                other=copy.deepcopy(case);other['oracleContract']=contract;oracle.verify_transaction_case(other)
                self.assertEqual(oracle.decide_transaction(data['initialState'],data['events'][0]),oracle.decide_transaction(data['initialState'],data['events'][0],contract=contract))

    def test_uuid_named_validity_diagnosis_phase_commands_and_order_observers(self):
        original=oracle.decide_transaction
        for case in self.uuid_cases():
            for diagnosis in ('stale-phase','invalid-state'):
                with patch.object(oracle,'decide_transaction',side_effect=lambda s,e,**kw:dict(state=copy.deepcopy(s),diagnosis=diagnosis,commands=[])):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(case)
            with patch.object(oracle,'transaction_state_valid',return_value=False):
                with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(case)
            for field in ('diagnosis','phase','commands'):
                def damaged(s,e,**kw):
                    result=original(s,e,**kw)
                    if field=='diagnosis':result['diagnosis']='observer-damaged'
                    elif field=='phase':result['state']['phase']='IDLE'
                    else:result['commands']=[dict(type='Apply',correlation=copy.deepcopy(e['correlation']))]
                    return result
                with patch.object(oracle,'decide_transaction',side_effect=damaged):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(case)
            if len(case['expected'])>1:
                bad=copy.deepcopy(case);bad['expected'][0],bad['expected'][-1]=bad['expected'][-1],bad['expected'][0]
                for i,row in enumerate(bad['expected']):row['step']=i
                with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(bad)
            for i,row in enumerate(case['expected']):
                bad=copy.deepcopy(case);del bad['expected'][i]
                with self.assertRaises(oracle.FixtureFormatError):oracle.verify_transaction_case(bad)
                if row['commands']:
                    for commands in ([],row['commands']*2):
                        bad=copy.deepcopy(case);bad['expected'][i]['commands']=copy.deepcopy(commands)
                        with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'):oracle.verify_transaction_case(bad)
        # Identical negative rows do not prove their mutual order; distinct final arm does.


    def _assert_uuid_recursive_full_state_command_and_validity_leaves(self, panel_index):
        cases=oracle.load_transaction_fixture(self.fixture); pool={}
        def collect(v):
            if isinstance(v,dict):
                for k,c in v.items():
                    if c is not None: pool[k]=copy.deepcopy(c)
                    collect(c)
            elif isinstance(v,list):
                for c in v: collect(c)
        def leaves(v,path=()):
            if isinstance(v,dict):
                for k,c in v.items(): yield from leaves(c,path+(k,))
            elif isinstance(v,list):
                for k,c in enumerate(v): yield from leaves(c,path+(k,))
            else: yield path,v
        for c in cases: collect(c['expected'])
        pool.update(originalFailure='FAILURE',rollbackFailure='ROLLBACK',failureReceipt=pool['completionReceipt'])
        observed=0
        for case in (cases[359+panel_index],cases[363+panel_index]):
            for path,value in leaves(case['expected']):
                key=path[-1]
                if key=='step': continue
                observed+=1
                bad=copy.deepcopy(case); part=bad['expected']
                for k in path[:-1]: part=part[k]
                if key=='type':
                    parent=bad['expected']
                    for k in path[:-2]: parent=parent[k]
                    parent[path[-2]]={'type':'Vanilla'} if value=='Pack' else dict(type='Pack',id='changed',treeSha256='x',contentSha256='y',importReceiptSha256='z') if value=='Vanilla' else dict(type='Rollback' if value=='Apply' else 'Apply',correlation=copy.deepcopy(pool['correlation']))
                else:
                    choices={'phase':('IDLE','ARMED'),'state':('CLEAR','ARMED'),'mode':('OFF','ON'),'operation':('MODE_ON','MODE_OFF'),'skinStamp':('7','8')}
                    part[key]=copy.deepcopy(pool[key]) if value is None else next(v for v in choices[key] if v!=value) if key in choices else not value if type(value) is bool else value+'changed'
                with self.subTest(case=case['caseId'],leaf=path):
                    with self.assertRaisesRegex(AssertionError,'full ordered transaction log mismatch'): oracle.verify_transaction_case(bad)
            bad=copy.deepcopy(case); bad['expected'][0]['commands']=[dict(type='Apply',correlation=copy.deepcopy(pool['correlation']))]
        self.assertEqual([566,478,918,478][panel_index],observed)


class HollowKnightSkinUuidShapeRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_uuid_shape_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinUuidLexicalGoldensTest._assert_uuid_recursive_full_state_command_and_validity_leaves(self, 0)


class HollowKnightSkinUuidAsciiRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_uuid_ascii_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinUuidLexicalGoldensTest._assert_uuid_recursive_full_state_command_and_validity_leaves(self, 1)


class HollowKnightSkinUuidWhitespaceRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_uuid_whitespace_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinUuidLexicalGoldensTest._assert_uuid_recursive_full_state_command_and_validity_leaves(self, 2)


class HollowKnightSkinUuidUnicodePositiveRecursiveGoldensTest(unittest.TestCase):
    fixture = HollowKnightSkinTransactionGoldensTest.fixture

    def test_uuid_unicode_and_positive_recursive_full_state_command_and_validity_leaves(self):
        HollowKnightSkinUuidLexicalGoldensTest._assert_uuid_recursive_full_state_command_and_validity_leaves(self, 3)

if __name__ == '__main__':
    unittest.main()
