import 'package:flutter/material.dart';

import '../../../../core/language/locales.dart';
import '../../../../widget/empty_state_widget.dart';

class ProfilePlaceholderScreen extends StatelessWidget {
  const ProfilePlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return EmptyStateWidget(
      icon: Icons.construction_outlined,
      title: 'navProfile'.tr,
      message: 'moduleComingSoon'.tr,
    );
  }
}
