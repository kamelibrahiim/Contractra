import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../core/language/locales.dart';
import '../core/theme/app_breakpoints.dart';
import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';
import '../widget/nav_rail_item.dart';
import 'text_style_helper.dart';

class ShellDestination {
  const ShellDestination({
    required this.labelKey,
    required this.icon,
    required this.path,
  });

  final String labelKey;
  final IconData icon;
  final String path;
}

/// Order must match the branch order in the router's shell route.
const List<ShellDestination> adminShellDestinations = [
  ShellDestination(labelKey: 'navDashboard', icon: Icons.dashboard_outlined, path: '/dashboard'),
  ShellDestination(labelKey: 'navParties', icon: Icons.groups_outlined, path: '/parties'),
  ShellDestination(labelKey: 'navOperations', icon: Icons.agriculture_outlined, path: '/operations'),
  ShellDestination(labelKey: 'navApprovals', icon: Icons.fact_check_outlined, path: '/approvals'),
  ShellDestination(labelKey: 'navReports', icon: Icons.assessment_outlined, path: '/reports'),
];

/// Persistent Admin frame: a side rail at [AppBreakpoints.large] and wider,
/// a menu-triggered drawer below it. Not permission-filtered yet — there is
/// no authenticated session to read permissions from.
class AppShellScreen extends StatelessWidget {
  const AppShellScreen({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  void _select(int index) => navigationShell.goBranch(
        index,
        initialLocation: index == navigationShell.currentIndex,
      );

  Widget _navList(ThemeModel theme, {VoidCallback? onSelected}) {
    return ListView(
      padding: AppSpacing.allMd,
      children: [
        Padding(
          padding: EdgeInsets.only(bottom: AppSpacing.lg, top: AppSpacing.sm),
          child: Text(
            'appTitle'.tr,
            style: TextStyleHelper.h4.copyWith(color: theme.textPrimaryColor),
          ),
        ),
        for (var i = 0; i < adminShellDestinations.length; i++)
          Padding(
            padding: EdgeInsets.only(bottom: AppSpacing.xs),
            child: NavRailItem(
              icon: adminShellDestinations[i].icon,
              label: adminShellDestinations[i].labelKey.tr,
              isActive: i == navigationShell.currentIndex,
              onTap: () {
                onSelected?.call();
                _select(i);
              },
            ),
          ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final width = MediaQuery.sizeOf(context).width;

    if (width >= AppBreakpoints.large) {
      return Scaffold(
        backgroundColor: theme.backgroundColor,
        body: Row(
          children: [
            Container(
              width: 260,
              color: theme.surfaceColor,
              child: SafeArea(child: _navList(theme)),
            ),
            VerticalDivider(width: 1, thickness: 1, color: theme.borderColor),
            Expanded(child: navigationShell),
          ],
        ),
      );
    }

    return Scaffold(
      backgroundColor: theme.backgroundColor,
      appBar: AppBar(
        backgroundColor: theme.surfaceColor,
        foregroundColor: theme.textPrimaryColor,
        elevation: 0,
        scrolledUnderElevation: 0,
        shape: Border(bottom: BorderSide(color: theme.borderColor)),
        title: Text(
          adminShellDestinations[navigationShell.currentIndex].labelKey.tr,
          style: TextStyleHelper.h4.copyWith(color: theme.textPrimaryColor),
        ),
      ),
      drawer: Drawer(
        backgroundColor: theme.surfaceColor,
        child: Builder(
          builder: (drawerContext) => SafeArea(
            child: _navList(theme, onSelected: () => Navigator.of(drawerContext).pop()),
          ),
        ),
      ),
      body: navigationShell,
    );
  }
}
