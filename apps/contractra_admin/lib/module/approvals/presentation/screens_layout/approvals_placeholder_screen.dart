import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../widget/empty_state_widget.dart';

class ApprovalsPlaceholderScreen extends StatelessWidget {
  const ApprovalsPlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return EmptyStateWidget(
      icon: Icons.construction_outlined,
      title: 'navApprovals'.tr,
      message: 'moduleComingSoon'.tr,
    );
  }
}
