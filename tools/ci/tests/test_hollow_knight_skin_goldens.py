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
        return [c for c in oracle.load_transaction_fixture(self.fixture) if c['oracleContract'] == oracle.TRANSACTION_CONTRACT]

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
        return [c for c in oracle.load_transaction_fixture(self.fixture) if c['oracleContract'] == oracle.TRANSACTION_ROLLBACK_CONTRACT]

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
        self.assertEqual(115, len([c for c in cases if c['oracleContract'] in (oracle.TRANSACTION_CONTRACT, oracle.TRANSACTION_ROLLBACK_CONTRACT)]))

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
        return [c for c in oracle.load_transaction_fixture(self.fixture) if c['oracleContract'] == oracle.TRANSACTION_FAILURE_CONTRACT]

    def test_failure_inventory_and_accepted115_byte_semantic_pins(self):
        raw = self.fixture.read_bytes(); suffix = b'\r\n  ]\r\n}\r\n'
        self.assertEqual('5066d2dd810e3c8538ecc4f9ae26927b24138fe2847b79f9375774fa37b5e7ac', hashlib.sha256(raw[:1528504] + suffix).hexdigest())
        cases = oracle.load_transaction_fixture(self.fixture); accepted = cases[:115]
        self.assertEqual(115, len(accepted)); self.assertEqual(241, sum(len(c['expected']) for c in accepted))
        self.assertEqual('1a0f282f324641738ee7b0c0b76f4430900ba7efd80139521b67cedd5423e48d', hashlib.sha256(json.dumps(accepted, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest())
        failure = self.failure_cases()
        self.assertEqual(23, len(failure)); self.assertEqual(136, sum(len(c['expected']) for c in failure)); self.assertEqual(138, len(cases))
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


if __name__ == '__main__':
    unittest.main()
