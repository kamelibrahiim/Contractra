import 'package:flutter/material.dart';

import '../../../../../core/theme/app_spacing.dart';

/// Tablet layout: the same single column, width-capped and centered.
class MediumLoginScreen extends StatelessWidget {
  const MediumLoginScreen({super.key, required this.form});

  final Widget form;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 480),
        child: Padding(padding: AppSpacing.horizontalLg, child: form),
      ),
    );
  }
}
