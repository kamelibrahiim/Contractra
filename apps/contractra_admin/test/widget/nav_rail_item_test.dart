import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/core/theme/theme_model.dart';
import 'package:contractra_admin/utilities/git_it.dart';
import 'package:contractra_admin/widget/nav_rail_item.dart';

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
  });

  Future<void> pumpItem(WidgetTester tester, {required bool isActive, VoidCallback? onTap}) {
    return tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => MaterialApp(
        home: BlocProvider(
          create: (_) => ThemeCubit(),
          child: Scaffold(
            body: SizedBox(
              width: 260,
              child: NavRailItem(
                icon: Icons.groups_outlined,
                label: 'Parties',
                isActive: isActive,
                onTap: onTap ?? () {},
              ),
            ),
          ),
        ),
      ),
    ));
  }

  Color iconColor(WidgetTester tester) =>
      tester.widget<Icon>(find.byIcon(Icons.groups_outlined)).color!;

  group('NavRailItem', () {
    testWidgets('active item uses the primary color and a filled background', (tester) async {
      await pumpItem(tester, isActive: true);
      final theme = tester.element(find.byType(NavRailItem)).read<ThemeCubit>().state.appTheme;

      expect(iconColor(tester), theme.primaryColor);
      final material = tester.widget<Material>(
        find.descendant(of: find.byType(NavRailItem), matching: find.byType(Material)),
      );
      expect(material.color, isNot(Colors.transparent));
    });

    testWidgets('inactive item uses the secondary text color and no background', (tester) async {
      await pumpItem(tester, isActive: false);
      final ThemeModel theme = tester.element(find.byType(NavRailItem)).read<ThemeCubit>().state.appTheme;

      expect(iconColor(tester), theme.textSecondaryColor);
      final material = tester.widget<Material>(
        find.descendant(of: find.byType(NavRailItem), matching: find.byType(Material)),
      );
      expect(material.color, Colors.transparent);
    });

    testWidgets('tapping calls onTap', (tester) async {
      var taps = 0;
      await pumpItem(tester, isActive: false, onTap: () => taps++);

      await tester.tap(find.text('Parties'));
      expect(taps, 1);
    });
  });
}
