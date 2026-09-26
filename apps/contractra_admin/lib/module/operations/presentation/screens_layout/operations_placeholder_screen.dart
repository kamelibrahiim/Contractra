import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../widget/empty_state_widget.dart';

class OperationsPlaceholderScreen extends StatelessWidget {
  const OperationsPlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return EmptyStateWidget(
      icon: Icons.construction_outlined,
      title: 'navOperations'.tr,
      message: 'moduleComingSoon'.tr,
    );
  }
}
