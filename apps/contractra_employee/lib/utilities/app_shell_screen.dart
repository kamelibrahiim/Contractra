import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../core/language/locales.dart';
import '../core/theme/app_theme.dart';
import 'text_style_helper.dart';

class ShellDestination {
  const ShellDestination({
    required this.labelKey,
    required this.icon,
    required this.activeIcon,
    required this.path,
  });

  final String labelKey;
  final IconData icon;
  final IconData activeIcon;
  final String path;
}

/// Engineer-facing tabs only; no reports, approvals, or system-wide views.
/// Order must match the branch order in the router's shell route.
const List<ShellDestination> employeeShellDestinations = [
  ShellDestination(
    labelKey: 'navMySites',
    icon: Icons.location_on_outlined,
    activeIcon: Icons.location_on,
    path: '/assignments',
  ),
  ShellDestination(
    labelKey: 'navExpenses',
    icon: Icons.receipt_long_outlined,
    activeIcon: Icons.receipt_long,
    path: '/expenses',
  ),
  ShellDestination(
    labelKey: 'navProfile',
    icon: Icons.person_outline,
    activeIcon: Icons.person,
    path: '/profile',
  ),
];

class AppShellScreen extends StatelessWidget {
  const AppShellScreen({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Scaffold(
      backgroundColor: theme.backgroundColor,
      body: navigationShell,
      bottomNavigationBar: DecoratedBox(
        decoration: BoxDecoration(
          border: Border(top: BorderSide(color: theme.borderColor)),
        ),
        child: NavigationBar(
          backgroundColor: theme.surfaceColor,
          indicatorColor: theme.primaryColor.withValues(alpha: 0.12),
          selectedIndex: navigationShell.currentIndex,
          onDestinationSelected: (index) => navigationShell.goBranch(
            index,
            initialLocation: index == navigationShell.currentIndex,
          ),
          labelTextStyle: WidgetStateProperty.resolveWith(
            (states) => TextStyleHelper.bodySm.copyWith(
              color: states.contains(WidgetState.selected)
                  ? theme.primaryColor
                  : theme.textSecondaryColor,
              fontWeight: states.contains(WidgetState.selected)
                  ? FontWeight.w600
                  : FontWeight.w400,
            ),
          ),
          destinations: [
            for (final d in employeeShellDestinations)
              NavigationDestination(
                icon: Icon(d.icon, color: theme.textSecondaryColor),
                selectedIcon: Icon(d.activeIcon, color: theme.primaryColor),
                label: d.labelKey.tr,
              ),
          ],
        ),
      ),
    );
  }
}
