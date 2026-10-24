import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/language/locales.dart';
import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/module/parties/data_layer/models/mock_parties.dart';
import 'package:contractra_admin/module/parties/data_layer/models/party_summary.dart';
import 'package:contractra_admin/module/parties/presentation/screens_layout/party_directory_screen.dart';
import 'package:contractra_admin/module/parties/presentation/widgets/party_list_item_widget.dart';
import 'package:contractra_admin/utilities/git_it.dart';
import 'package:contractra_admin/widget/empty_state_widget.dart';

const documentedRoleCodes = {
  'Engineer', 'Worker', 'Supplier', 'Contractor',
  'Debtor', 'Creditor', 'Partner', 'Company',
};

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    // A cached asset future from a previous test's zone never completes here.
    rootBundle.clear();
  });

  Future<void> pump(WidgetTester tester, Widget child, {Size size = const Size(400, 800)}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => BlocProvider(
        create: (_) => ThemeCubit(),
        child: MaterialApp(
          locale: const Locale('en'),
          localizationsDelegates: const [
            AppLocalizationsDelegate(),
            DefaultMaterialLocalizations.delegate,
            DefaultWidgetsLocalizations.delegate,
          ],
          home: Scaffold(body: child),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(Scaffold).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  Color statusDotColor(WidgetTester tester) {
    final dot = tester.widget<Container>(find.descendant(
      of: find.byKey(const Key('party_status')),
      matching: find.byType(Container),
    ));
    return (dot.decoration! as BoxDecoration).color!;
  }

  group('mock data', () {
    test('uses only documented role codes and has 8-12 records', () {
      expect(mockParties.length, inInclusiveRange(8, 12));
      for (final party in mockParties) {
        expect(documentedRoleCodes.containsAll(party.roleCodes), isTrue, reason: party.id);
      }
    });
  });

  group('PartyListItemWidget', () {
    testWidgets('renders a party with 1 role without overflow', (tester) async {
      await pump(tester, const PartyListItemWidget(
        party: PartySummary(id: 'a', displayName: 'One Role', roleCodes: ['Engineer'], status: PartyStatus.active),
      ));

      expect(find.text('One Role'), findsOneWidget);
      expect(find.text('Engineer'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('renders a party with 3 roles without overflow', (tester) async {
      await pump(tester, const PartyListItemWidget(
        party: PartySummary(
          id: 'b',
          displayName: 'Three Roles With A Fairly Long Display Name',
          roleCodes: ['Contractor', 'Partner', 'Creditor'],
          status: PartyStatus.active,
        ),
      ), size: const Size(360, 800));

      for (final label in ['Contractor', 'Partner', 'Creditor']) {
        expect(find.text(label), findsOneWidget);
      }
      expect(tester.takeException(), isNull);
    });

    testWidgets('each status renders a distinct indicator color', (tester) async {
      final colors = <PartyStatus, Color>{};
      for (final status in PartyStatus.values) {
        await pump(tester, PartyListItemWidget(
          party: PartySummary(id: status.name, displayName: 'P', roleCodes: const [], status: status),
        ));
        colors[status] = statusDotColor(tester);
      }

      expect(colors[PartyStatus.needsClassification], isNot(colors[PartyStatus.active]));
      expect(colors[PartyStatus.inactive], isNot(colors[PartyStatus.active]));
      expect(colors[PartyStatus.inactive], isNot(colors[PartyStatus.needsClassification]));
    });
  });

  group('PartyDirectoryScreen', () {
    testWidgets('renders the mock list', (tester) async {
      await pump(tester, const PartyDirectoryScreen(), size: const Size(1000, 3000));

      expect(find.byType(PartyListItemWidget), findsNWidgets(mockParties.length));
      expect(find.byType(EmptyStateWidget), findsNothing);
    });

    testWidgets('shows EmptyStateWidget for an empty list', (tester) async {
      await pump(tester, const PartyDirectoryScreen(parties: []));

      expect(find.byType(EmptyStateWidget), findsOneWidget);
      expect(find.byType(PartyListItemWidget), findsNothing);
    });
  });
}
