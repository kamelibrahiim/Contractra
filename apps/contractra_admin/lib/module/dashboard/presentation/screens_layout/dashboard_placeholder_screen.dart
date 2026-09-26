import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../widget/empty_state_widget.dart';

class DashboardPlaceholderScreen extends StatelessWidget {
  const DashboardPlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return EmptyStateWidget(
      icon: Icons.construction_outlined,
      title: 'navDashboard'.tr,
      message: 'moduleComingSoon'.tr,
    );
  }
}
