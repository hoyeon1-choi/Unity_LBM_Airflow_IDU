#ifndef config_h
#define config_h

#ifdef __cplusplus
extern "C" {
#endif

#include "CFMU/Model/ModelConfigBase.h"

#define MODEL_IDENTIFIER "Multi_V_S__Set_CFMU"
#define INSTANTIATION_TOKEN "{8754A561-0453-4AC1-ADF8-CB644C3AFCEB}"

#define EVENT_UPDATE

#define NUMBER_OF_FLOAT64_VARIABLE 102
#define NUMBER_OF_INT32_VARIABLE 5
#define NUMBER_OF_BOOLEAN_VARIABLE 1
#define NUMBER_OF_STRING_VARIABLE 6

#define GET_FLOAT64
#define GET_INT32
#define GET_BOOLEAN
#define GET_STRING
#define SET_FLOAT64
#define SET_INT32
#define SET_BOOLEAN
#define SET_STRING

#include <stdbool.h>

typedef struct {
	double* realBufferPtr;
	int* integerBufferPtr;
	bool* booleanBufferPtr;
	const char** stringBufferPtr;
} ModelData;

extern double FIXED_SOLVER_STEP;

#ifdef __cplusplus
}
#endif

#endif /* config_h */
