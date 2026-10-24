import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../widget/empty_state_widget.dart';

class ReportsPlaceholderScreen extends StatelessWidget {
  const ReportsPlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return EmptyStateWidget(
      icon: Icons.construction_outlined,
      title: 'navReports'.tr,
      message: 'moduleComingSoon'.tr,
    );
  }
}
