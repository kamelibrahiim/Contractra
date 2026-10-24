import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/widget/app_card.dart';
import 'package:contractra_admin/utilities/git_it.dart';

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
  });

  Widget wrap(Widget child) => ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(
          home: BlocProvider(
            create: (_) => ThemeCubit(),
            child: Scaffold(body: Center(child: child)),
          ),
        ),
      );

  group('AppCard', () {
    testWidgets('renders with a small placeholder child without overflow',
        (tester) async {
      await tester.pumpWidget(wrap(const AppCard(child: SizedBox(height: 20))));

      expect(tester.takeException(), isNull);
      expect(find.byType(AppCard), findsOneWidget);
    });

    testWidgets('renders with a medium placeholder child without overflow',
        (tester) async {
      await tester.pumpWidget(
        wrap(AppCard(
          child: Column(
            children: List.generate(5, (_) => const SizedBox(height: 20)),
          ),
        )),
      );

      expect(tester.takeException(), isNull);
      expect(find.byType(AppCard), findsOneWidget);
    });

    testWidgets('renders with a large placeholder child without overflow',
        (tester) async {
      await tester.pumpWidget(
        wrap(SizedBox(
          width: 300,
          child: AppCard(
            child: Column(
              children: List.generate(20, (_) => const SizedBox(height: 20)),
            ),
          ),
        )),
      );

      expect(tester.takeException(), isNull);
      expect(find.byType(AppCard), findsOneWidget);
    });

    testWidgets('custom padding overrides the default', (tester) async {
      await tester.pumpWidget(
        wrap(const AppCard(
          padding: EdgeInsets.all(2),
          child: SizedBox(height: 10, width: 10),
        )),
      );

      final padding = tester.widget<Padding>(find.byType(Padding).first);
      expect(padding.padding, const EdgeInsets.all(2));
    });
  });
}
