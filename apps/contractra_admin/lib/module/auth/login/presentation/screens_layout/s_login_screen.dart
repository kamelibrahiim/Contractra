import 'package:flutter/material.dart';

import '../../../../../core/theme/app_spacing.dart';

/// Narrow layout: the form alone, centered and width-capped.
class SmallLoginScreen extends StatelessWidget {
  const SmallLoginScreen({super.key, required this.form});

  final Widget form;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: SingleChildScrollView(
        padding: AppSpacing.allLg,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 440),
          child: form,
        ),
      ),
    );
  }
}
