// Loaded before every test file. The framework ships partially compiled, so a spec that imports a
// service depending on HttpClient falls back to the JIT compiler; without this the suite fails at
// import time with "needs to be compiled using the JIT compiler" rather than at a useful assertion.
import '@angular/compiler';
