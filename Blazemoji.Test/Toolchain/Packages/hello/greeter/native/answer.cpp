// The native half of the greeter: one function, bound to the type method 🎲 of the class 🙋.
// A function bound to a type method takes the class's info first.

#include "runtime/Runtime.h"
#include "answer.h"

extern "C" runtime::Integer helloGreeterAnswer(runtime::ClassInfo*) {
    return HELLO_GREETER_ANSWER;
}
