import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../widget/empty_state_widget.dart';

class ExpensesPlaceholderScreen extends StatelessWidget {
  const ExpensesPlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return EmptyStateWidget(
      icon: Icons.construction_outlined,
      title: 'navExpenses'.tr,
      message: 'moduleComingSoon'.tr,
    );
  }
}
